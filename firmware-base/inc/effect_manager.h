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

#include "ffb_effects.h"
#include "biquad_lp.h"

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

    // Filtros lowpass (biquad RBJ) por tipo de condition — mata os PICOS do Damper (velocidade
    // ruidosa) e do Inertia (derivada DUPLA) a 1kHz. Defaults do OpenFFBoard como ponto de partida:
    // damper 30Hz/Q0.4, inertia 15Hz/Q0.2, friction 50Hz/Q0.2 (fs=1000). Spring NÃO filtra (posição,
    // baixo ruído). Estado persistente entre ticks → resetado em reset()/freeBlock().
    drivelab::BiquadLP m_damperFilter, m_inertiaFilter, m_frictionFilter;

    // Reserva de bloco (2026-07-29, fix 400Hz): distinto de "configurado"/"ativo".
    // Create New Effect RESERVA um bloco livre (allocateBlock) ANTES do host mandar
    // o Set Effect; Block Free / reset liberam. Antes a alocação era um contador que
    // dava wrap em 40 sem reusar liberados → a 400Hz o ACC churna efeitos, bate no
    // wrap e recebe um bloco reusado/errado → trava. Agora reusamos o 1º livre e o
    // Block Load reporta o pool REAL. Ver [[drivelab-game-compat-a0]].
    bool m_allocated[kEffectSlots] = {false};
    // Comeca LIGADO: um jogo que nunca mande "habilitar" (a maioria) tem de funcionar.
    bool m_ffbLigado = true;

    // Estado p/ estimar aceleração (efeito Inertia) entre chamadas de
    // computeForce — um único "sensor" compartilhado por todos os slots
    // (não há um por-efeito porque pos/vel vêm do eixo físico único).
    float m_prevVel = 0.0f;
    uint32_t m_prevMs = 0;
    bool m_hasPrev = false;

    // ---- Normalização física (metric em [-1,1] p/ efeitos Condition) ----
public:
    EffectManager() {
        // Cortes do OpenFFBoard (ajustar na bancada). fs=1000Hz (laço FFB).
        m_damperFilter.configure(30.0f, 0.4f, 1000.0f);
        m_inertiaFilter.configure(15.0f, 0.2f, 1000.0f);
        m_frictionFilter.configure(50.0f, 0.2f, 1000.0f);
    }

    // Defaults das escalas de normalização. Públicos p/ referência/testes; o ponto de partida quando
    // ninguém configura. AJUSTAR na bancada os de velocidade/aceleração.
    static constexpr float kMaxPosRad = 3.14159265358979323846f; // curso físico default (±180°)
    static constexpr float kMaxVel = 20.0f;                       // velocidade angular máx. default (rad/s)
    static constexpr float kMaxAccel = 500.0f;                    // aceleração angular máx. default (rad/s^2)

    // Configura as escalas em runtime. kMaxPosRad DEVE casar com a DOR (endstop.rangeRad = curso REAL do
    // eixo até o batente) — senão o Spring/Damper do JOGO saem com magnitude errada (saturam no lugar
    // errado). applyCfgToEngine chama setPosRange(rangeRad). Ignora valores não-positivos.
    void setPosRange(float maxPosRad)  { if (maxPosRad > 1e-4f) m_maxPosRad = maxPosRad; }
    void setVelRange(float maxVel)     { if (maxVel > 1e-4f)    m_maxVel = maxVel; }
    void setAccelRange(float maxAccel) { if (maxAccel > 1e-4f)  m_maxAccel = maxAccel; }

    // Ganhos por tipo de efeito Condition (0-200%, default 100% = neutro) —
    // knobs do usuário p/ ajustar a "sensação" do Spring/Damper/Friction/
    // Inertia que o JOGO manda, sem alterar o que o jogo pediu (posCoeff/
    // negCoeff/etc. do próprio efeito). Aplicado no fim de conditionForce().
    void setTypeGains(uint8_t spring, uint8_t damper, uint8_t friction, uint8_t inertia) {
        m_gainSpring = spring; m_gainDamper = damper;
        m_gainFriction = friction; m_gainInertia = inertia;
    }

private:
    float m_maxPosRad = kMaxPosRad;   // curso do eixo (DOR/2) — setPosRange() casa com a config
    float m_maxVel    = kMaxVel;
    float m_maxAccel  = kMaxAccel;

    uint8_t m_gainSpring = 100, m_gainDamper = 100, m_gainFriction = 100, m_gainInertia = 100;

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
                metric = clamp1(posRad / m_maxPosRad - e.centerOffset / 32767.0f);
                break;
            case FxType::Damper:
                metric = clamp1(velRadPerSec / m_maxVel);
                break;
            case FxType::Friction:
                metric = (velRadPerSec > 0.0f) ? 1.0f : (velRadPerSec < 0.0f ? -1.0f : 0.0f);
                break;
            case FxType::Inertia:
                metric = clamp1(accel / m_maxAccel);
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

        float g = 1.0f;
        switch (e.type) {
            case FxType::Spring:   g = m_gainSpring   / 100.0f; break;
            case FxType::Damper:   g = m_gainDamper   / 100.0f; break;
            case FxType::Friction: g = m_gainFriction / 100.0f; break;
            case FxType::Inertia:  g = m_gainInertia  / 100.0f; break;
            default: break;
        }
        return raw * g;
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
            // O campo e uma MASCARA DE BITS, e os seis comandos importam — tratavamos dois.
            // Ignorar "continuar" era o que deixava a forca sem voltar depois da tela de
            // configuracao do jogo.
            const uint8_t mask = buf[1];
            if (mask & 0x08) { reset(); m_ffbLigado = true; }  // reiniciar: limpa e volta pronto
            if (mask & 0x02) stopAll();                        // desabilitar
            if (mask & 0x04) stopAll();                        // parar tudo
            if (mask & 0x10) stopAll();                        // pausar
            if (mask & 0x01) resumeAll();                      // habilitar
            if (mask & 0x20) resumeAll();                      // continuar
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
                m_ffbLigado = true;   // mandar tocar vale como religar: nao ficar mudo por um bit
                break;
            case 2: // startSolo — toca ESTE e cala os OUTROS
                // ⚠️ Nao usar stopAll() aqui: agora ele e o interruptor GERAL da saida, e
                // "tocar sozinho" quer silenciar os outros efeitos, nao a base inteira.
                for (int k = 0; k < kEffectSlots; ++k) m_slots[k].active = false;
                m_slots[s].active = true;
                m_slots[s].startMs = nowMs;
                m_ffbLigado = true;
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
        m_allocated[s] = false;   // libera a reserva (o bloco volta ao pool)
    }

    // Reserva o 1º bloco LIVRE p/ um Create New Effect. Devolve o índice 1-based
    // (1..kEffectSlots) ou 0 se o pool estiver cheio (host deve tratar como falha).
    // Limpa o slot p/ o estado default — o Set Effect seguinte preenche os params.
    uint8_t allocateBlock() {
        for (int i = 0; i < kEffectSlots; ++i) {
            if (!m_allocated[i]) {
                m_allocated[i] = true;
                m_slots[i] = FxEffect{};
                return static_cast<uint8_t>(i + 1);
            }
        }
        return 0; // pool cheio
    }

    // Nº de blocos reservados no momento (p/ o RID_PID_POOL/Block Load reportar o
    // pool REAL disponível ao host).
    int usedBlocks() const {
        int n = 0;
        for (int i = 0; i < kEffectSlots; ++i) if (m_allocated[i]) ++n;
        return n;
    }

    // Limpa TODOS os slots pro estado default.
    void reset() {
        m_ffbLigado = true;
        for (int i = 0; i < kEffectSlots; ++i) {
            m_slots[i] = FxEffect{};
            m_allocated[i] = false;
        }
        m_damperFilter.reset(); m_inertiaFilter.reset(); m_frictionFilter.reset();
    }

    // Desativa todos os slots, preservando os parâmetros.
    // ============================================================================================
    // PARAR A FORCA E UM INTERRUPTOR, NAO UM APAGADOR
    // ============================================================================================
    // Antes, "parar tudo" desligava CADA efeito, e para voltar cada um precisava receber um novo
    // comando de tocar. So que o jogo nao remanda tocar: ele manda "continuar" e espera que o que
    // ja estava montado volte a valer. Resultado: ao entrar na tela de configuracao e sair, a forca
    // nunca voltava — o jogo seguia enviando, nos seguiamos recebendo, e nada saia (bancada,
    // 21/08/2026).
    //
    // As duas implementacoes de referencia usam um interruptor global: os efeitos continuam
    // montados e ativos, so a saida e silenciada. Religar devolve tudo de uma vez. E o que fazemos
    // aqui.
    void stopAll() { m_ffbLigado = false; }
    void resumeAll() { m_ffbLigado = true; }
    bool ffbLigado() const { return m_ffbLigado; }

    const FxEffect& slot(int i) const { return m_slots[i]; } // p/ testes

    // Fator de direção do eixo X (axisMagnitudes[0]) do bloco, decodificando o campo
    // Direction do Set Effect — convenção polar do HID PID (sin/-cos, ou
    // por-eixo linear, + o fallback de 1-eixo com cos(phaseX): +1 a 0°, -1 a 180°). É o que
    // dá o SENTIDO correto à força constante do jogo (sem isso, força fixa/errada → runaway).
    float axisDirFactor(int block1based) const {
        const int s = block1based - 1;
        if (s < 0 || s >= kEffectSlots) return 1.0f;
        const FxEffect& e = m_slots[s];
        const uint8_t X_AXIS_ENABLE = 1;
        const uint8_t directionEnableMask = 2;                  // 1 << axisCount(1)
        const float directionX = (float)(uint16_t)e.directionCentideg;   // 0..36000 centideg
        const bool directionEnable = (e.enableAxis & directionEnableMask) != 0;
        const float phaseX = 2.0f * (float)M_PI * (directionX / 36000.0f);
        float m0 = directionEnable ? std::sin(phaseX)
                 : ((e.enableAxis & X_AXIS_ENABLE) ? (directionX - 18000.0f) / 18000.0f : 0.0f);
        float m1 = directionEnable ? -std::cos(phaseX) : 0.0f;  // eixo Y (inexistente no volante)
        if (m0 == 0.0f && m1 == 0.0f) {                         // direção "north/south" projetaria só em Y
            float fb = directionEnable ? std::cos(phaseX) : 1.0f;
            if (fb == 0.0f) fb = 1.0f;
            m0 = fb;                                            // redireciona pro eixo X (o do volante)
        }
        return m0;
    }

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
            if (!m_ffbLigado) continue;   // saida silenciada: os efeitos ficam montados
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
                    f = conditionForce(e, posRad, velRadPerSec, accel);           // posição, baixo ruído: sem filtro
                    break;
                case FxType::Damper:
                    f = m_damperFilter.process(conditionForce(e, posRad, velRadPerSec, accel));   // 30Hz
                    break;
                case FxType::Friction:
                    f = m_frictionFilter.process(conditionForce(e, posRad, velRadPerSec, accel)); // 50Hz
                    break;
                case FxType::Inertia:
                    f = m_inertiaFilter.process(conditionForce(e, posRad, velRadPerSec, accel));  // 15Hz (mata o pico da derivada dupla)
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
