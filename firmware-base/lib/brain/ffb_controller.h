// ============================================================================
//  DriveLab Firmware
//  ffb_controller.h — Orquestra um passo da malha FFB: lê HAL, calcula torque seguro, comanda o motor.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================
//
// O "cérebro" completo: junta a matemática pura (ffb_math.h) com as bordas de hardware
// (hal.h). Um passo = ler encoder + corrente → calcular torque seguro → comandar o motor.
// Independente de plataforma: no firmware recebe as impls reais; no teste, mocks.
// SEGURANÇA: sobrecorrente desarma (latched); enquanto !enabled o motor fica desligado.
#pragma once

#include "ffb_math.h"
#include "hal.h"

namespace drivelab {

class FfbController {
public:
    ForceConfig   force;
    EndstopConfig endstop;
    float currentLimitA = 8.0f;   ///< corte de segurança por fase
    bool  enabled = false;        ///< SetForceEnabled (host) liga/desliga a força
    bool  tripped = false;        ///< desarme latched por sobrecorrente

    /// Um passo da malha. Retorna o torque comandado (Nm) — útil p/ testar.
    float step(int32_t hostForce, IEncoder& enc, ICurrentSense& sense, IMotor& motor) {
        if (!enabled || tripped) {
            motor.disable();
            return 0.0f;
        }

        float ia, ib, ic;
        sense.readPhaseCurrents(ia, ib, ic);
        if (overCurrent(ia, ib, ic, currentLimitA)) {
            tripped = true;       // latched: só religa via rearme explícito (rearm())
            motor.disable();
            return 0.0f;
        }

        const float t = finalTorque(hostForce, enc.positionRad(), force, endstop);
        motor.setTorque(t);
        return t;
    }

    /// Rearma após um desarme por sobrecorrente (ex.: comando do host depois de investigar).
    void rearm() { tripped = false; }
};

}  // namespace drivelab
