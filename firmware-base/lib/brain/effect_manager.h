// ============================================================================
//  DriveLab Firmware
//  effect_manager.h — Banco de slots de efeitos FFB: roteia OUT reports PID
//  (Set Effect/Envelope/Condition/Periodic/Constant/Ramp, Effect Operation,
//  Block Free, Device Control) pro slot certo e gerencia o ciclo de vida
//  (start/startSolo/stop/free/reset). Puro, host-testável, sem motor —
//  avaliação de força (computeForce) é responsabilidade de outro módulo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include "../base_shared/ffb_effects.h"

#include <cmath>
#include <cstdint>

#ifndef M_PI
#define M_PI 3.14159265358979323846
#endif

// Deve cobrir todo o espaço de effect-block que o handshake Create New Effect do
// m5 distribui (kMaxEffectBlocks=40): se for menor, blocos além do limite viram
// no-op silencioso (Block Load responde Success mas o efeito não gera força).
static constexpr int kEffectSlots = 40;

class EffectManager {
    FxEffect m_slots[kEffectSlots];

    // Estado p/ estimar aceleração (efeito Inertia) entre chamadas de
    // computeForce — um único "sensor" compartilhado por todos os slots
    // (não há um por-efeito porque pos/vel vêm do eixo físico único).
    float m_prevVel = 0.0f;
    uint32_t m_prevMs = 0;
    bool m_hasPrev = false;

    // ---- Normalização física (metric em [-1,1] p/ efeitos Condition) ----
    // AJUSTAR na bancada: valores placeholder até medir o curso real do
    // volante/pedal e a velocidade angular máxima observada.
public:
    static constexpr float kMaxPosRad = 3.14159265358979323846f; // AJUSTAR na bancada: curso físico (±180°) até endstop
    static constexpr float kMaxVel = 20.0f;                       // AJUSTAR na bancada: velocidade angular máx. observada (rad/s)
    static constexpr float kMaxAccel = 500.0f;                    // AJUSTAR na bancada: aceleração angular máx. observada (rad/s^2)

private:
    static float clamp1(float v) {
        if (v > 1.0f) return 1.0f;
        if (v < -1.0f) return -1.0f;
        return v;
    }

    // Fator de envelope (0..1) — só se aplica a Constant/Ramp/Periodic.
    // attackLevel/fadeLevel são frações [0,32767]->[0,1] do nível de partida
    // (attack) / chegada (fade); sustain = 1.0 entre os dois trechos.
    static float envelopeFactor(const FxEffect& e, uint32_t nowMs) {
        if (e.attackMs == 0 && e.fadeMs == 0) return 1.0f;

        const uint32_t t = nowMs - e.startMs;
        float factor = 1.0f;

        if (e.attackMs > 0 && t < e.attackMs) {
            const float p = (float)t / (float)e.attackMs;
            const float lvl = e.attackLevel / 32767.0f;
            factor = lvl + p * (1.0f - lvl);
        }

        if (e.durationMs > 0 && e.fadeMs > 0) {
            const uint32_t fadeStart = (e.fadeMs >= e.durationMs) ? 0 : (e.durationMs - e.fadeMs);
            if (t >= fadeStart) {
                float p = (float)(t - fadeStart) / (float)e.fadeMs;
                if (p > 1.0f) p = 1.0f;
                const float lvl = e.fadeLevel / 32767.0f;
                factor = 1.0f + p * (lvl - 1.0f);
            }
        }

        return factor;
    }

    // Força "crua" (±32767) de um efeito Constant/Ramp/Periodic, sem
    // envelope/gain (aplicados depois, no chamador).
    static float baseForceTimeDomain(const FxEffect& e, uint32_t nowMs) {
        switch (e.type) {
            case FxType::Constant:
                return (float)e.magnitude;

            case FxType::Ramp: {
                float p = 1.0f;
                if (e.durationMs > 0) {
                    p = (float)(nowMs - e.startMs) / (float)e.durationMs;
                    if (p < 0.0f) p = 0.0f;
                    if (p > 1.0f) p = 1.0f;
                }
                return (float)e.rampStart + p * (float)(e.rampEnd - e.rampStart);
            }

            case FxType::Square:
            case FxType::Sine:
            case FxType::Triangle:
            case FxType::SawtoothUp:
            case FxType::SawtoothDown: {
                double x = 0.0;
                if (e.period > 0) {
                    const uint32_t t = nowMs - e.startMs;
                    const double phaseFrac = e.phase / 36000.0; // centideg -> voltas
                    x = std::fmod((double)t / (double)e.period + phaseFrac, 1.0);
                    if (x < 0.0) x += 1.0;
                }

                double w = 0.0;
                switch (e.type) {
                    case FxType::Sine:         w = std::sin(2.0 * M_PI * x); break;
                    case FxType::Square:       w = (x < 0.5) ? 1.0 : -1.0; break;
                    case FxType::Triangle:     w = (x < 0.5) ? (4.0 * x - 1.0) : (3.0 - 4.0 * x); break;
                    case FxType::SawtoothUp:   w = 2.0 * x - 1.0; break;
                    case FxType::SawtoothDown: w = 1.0 - 2.0 * x; break;
                    default: break; // inalcançável (guardado pelo switch externo)
                }

                return (float)(w * (double)e.magnitude16) + (float)e.offset;
            }

            default:
                return 0.0f;
        }
    }

    // Força "crua" (±32767) de um efeito Condition (Spring/Damper/
    // Friction/Inertia), usando posRad/velRadPerSec/accel já normalizados.
    float conditionForce(const FxEffect& e, float posRad, float velRadPerSec, float accel) const {
        float metric = 0.0f;

        switch (e.type) {
            case FxType::Spring:
                metric = clamp1(posRad / kMaxPosRad - e.centerOffset / 32767.0f);
                break;
            case FxType::Damper:
                metric = clamp1(velRadPerSec / kMaxVel);
                break;
            case FxType::Friction:
                metric = (velRadPerSec > 0.0f) ? 1.0f : (velRadPerSec < 0.0f ? -1.0f : 0.0f);
                break;
            case FxType::Inertia:
                metric = clamp1(accel / kMaxAccel);
                break;
            default:
                return 0.0f;
        }

        const float deadThresh = e.deadBand / 32767.0f;
        if (std::fabs(metric) < deadThresh) return 0.0f;

        const float coeff = (metric > 0.0f) ? (float)e.posCoeff : (float)e.negCoeff;
        float raw = -(coeff / 32767.0f) * metric * 32767.0f; // = -coeff*metric

        const float sat = (raw >= 0.0f) ? (float)e.posSat : (float)e.negSat;
        if (std::fabs(raw) > sat) raw = (raw >= 0.0f) ? sat : -sat;

        return raw;
    }

public:
    // Roteia UM OUT report host->device pro slot certo (por effectBlockIndex
    // = buf[1], 1-based). buf[0] é o ReportID; dispatch:
    //   0x01 SetEffect, 0x02 Envelope, 0x03 Condition, 0x04 Periodic,
    //   0x05 Constant, 0x06 Ramp -> fxDecode* no slot (demais campos do slot
    //     ficam intocados); NÃO ativa o efeito.
    //   0x0A EffectOperation -> operation(buf[1], buf[2], nowMs)
    //   0x0B BlockFree       -> freeBlock(buf[1])
    //   0x0C DeviceControl   -> buf[1] é BITMASK: bit 0x08 -> reset(),
    //     senão bit 0x04 -> stopAll()
    //   demais IDs -> ignora
    void handleReport(const uint8_t* buf, uint16_t len, uint32_t nowMs) {
        if (buf == nullptr || len < 2) return;

        const uint8_t reportId = buf[0];

        if (reportId == 0x0C) {
            const uint8_t mask = buf[1];
            if (mask & 0x08) {
                reset();
            } else if (mask & 0x04) {
                stopAll();
            }
            return;
        }

        if (reportId == 0x0A) {
            if (len < 3) return;
            operation(buf[1], buf[2], nowMs);
            return;
        }

        if (reportId == 0x0B) {
            freeBlock(buf[1]);
            return;
        }

        const int s = static_cast<int>(buf[1]) - 1;
        if (s < 0 || s >= kEffectSlots) return;

        switch (reportId) {
            case 0x01: fxDecodeSetEffect(buf, len, m_slots[s]); m_slots[s].block = buf[1]; break;
            case 0x02: fxDecodeEnvelope(buf, len, m_slots[s]);  m_slots[s].block = buf[1]; break;
            case 0x03: fxDecodeCondition(buf, len, m_slots[s]); m_slots[s].block = buf[1]; break;
            case 0x04: fxDecodePeriodic(buf, len, m_slots[s]);  m_slots[s].block = buf[1]; break;
            case 0x05: fxDecodeConstant(buf, len, m_slots[s]);  m_slots[s].block = buf[1]; break;
            case 0x06: fxDecodeRamp(buf, len, m_slots[s]);      m_slots[s].block = buf[1]; break;
            default: break; // ID desconhecido -> ignora
        }
    }

    // EffectOperation: state 1=start, 2=startSolo (para os demais e inicia
    // só este), 3=stop. block1based fora de faixa -> ignora sem crash.
    void operation(uint8_t block1based, uint8_t state, uint32_t nowMs) {
        const int s = static_cast<int>(block1based) - 1;
        if (s < 0 || s >= kEffectSlots) return;

        switch (state) {
            case 1: // start
                m_slots[s].active = true;
                m_slots[s].startMs = nowMs;
                break;
            case 2: // startSolo
                stopAll();
                m_slots[s].active = true;
                m_slots[s].startMs = nowMs;
                break;
            case 3: // stop
                m_slots[s].active = false;
                break;
            default:
                break; // estado desconhecido -> ignora
        }
    }

    // Limpa o slot pro estado default (type=None, inativo). block1based fora
    // de faixa -> ignora sem crash.
    void freeBlock(uint8_t block1based) {
        const int s = static_cast<int>(block1based) - 1;
        if (s < 0 || s >= kEffectSlots) return;
        m_slots[s] = FxEffect{};
    }

    // Limpa TODOS os slots pro estado default.
    void reset() {
        for (int i = 0; i < kEffectSlots; ++i) {
            m_slots[i] = FxEffect{};
        }
    }

    // Desativa todos os slots, preservando os parâmetros.
    void stopAll() {
        for (int i = 0; i < kEffectSlots; ++i) {
            m_slots[i].active = false;
        }
    }

    const FxEffect& slot(int i) const { return m_slots[i]; } // p/ testes

    // Avalia TODOS os slots ativos e não-expirados, soma suas forças e
    // devolve o resultado na escala [-255,255] (mesma da força constante
    // reconstruída, p/ o engine somar direto). Puro: nowMs é explícito, não
    // lê clock nenhum — testável sem placa.
    //
    // posRad/velRadPerSec: posição/velocidade angulares do eixo (rad,
    // rad/s), usadas só pelos efeitos Condition (Spring/Damper/Friction/
    // Inertia); normalizadas por kMaxPosRad/kMaxVel/kMaxAccel (placeholders
    // "AJUSTAR na bancada").
    float computeForce(float posRad, float velRadPerSec, uint32_t nowMs) {
        // Estima aceleração (p/ Inertia) a partir da velocidade da chamada
        // anterior. Guarda o estado ANTES de perturbá-lo com este cálculo.
        float accel = 0.0f;
        if (m_hasPrev) {
            const uint32_t dtMs = nowMs - m_prevMs;
            if (dtMs > 0) {
                accel = (velRadPerSec - m_prevVel) / ((float)dtMs / 1000.0f);
            }
        }
        m_prevVel = velRadPerSec;
        m_prevMs = nowMs;
        m_hasPrev = true;

        if (!std::isfinite(posRad)) posRad = 0.0f;
        if (!std::isfinite(velRadPerSec)) velRadPerSec = 0.0f;
        if (!std::isfinite(accel)) accel = 0.0f;

        int32_t acc = 0;

        for (int i = 0; i < kEffectSlots; ++i) {
            const FxEffect& e = m_slots[i];
            if (!e.active) continue;
            if (e.durationMs > 0 && (nowMs - e.startMs) >= e.durationMs) continue; // expirado

            // Constant force já flui pelo ForceReconstructor (hostF, SP1) —
            // somá-lo de novo aqui duplicaria a força constante do jogo.
            if (e.type == FxType::Constant) continue;

            float f;
            switch (e.type) {
                case FxType::Ramp:
                case FxType::Square:
                case FxType::Sine:
                case FxType::Triangle:
                case FxType::SawtoothUp:
                case FxType::SawtoothDown:
                    f = baseForceTimeDomain(e, nowMs) * envelopeFactor(e, nowMs);
                    break;

                case FxType::Spring:
                case FxType::Damper:
                case FxType::Friction:
                case FxType::Inertia:
                    f = conditionForce(e, posRad, velRadPerSec, accel);
                    break;

                default:
                    f = 0.0f;
                    break;
            }

            f *= e.gain / 255.0f;

            if (!std::isfinite(f)) f = 0.0f;
            acc += (int32_t)f;
        }

        if (acc > 32767) acc = 32767;
        if (acc < -32767) acc = -32767;

        return (float)acc * 255.0f / 32767.0f;
    }
};
