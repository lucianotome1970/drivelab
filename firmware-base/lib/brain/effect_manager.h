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

static constexpr int kEffectSlots = 20;

class EffectManager {
    FxEffect m_slots[kEffectSlots];

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
};
