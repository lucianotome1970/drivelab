// ============================================================================
//  DriveLab Firmware
//  ffb_controller.h — Orquestra um passo da malha FFB: lê HAL, calcula torque seguro, comanda o motor.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
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
    EffectConfig  effect;         ///< mola/damper/atrito do device (do encoder)
    EndstopConfig endstop;
    float currentLimitA    = 8.0f; ///< corte de segurança por fase
    float maxSlewNmPerStep = 0.0f; ///< limite de slew-rate por passo (0 = desligado)
    bool  enabled = false;         ///< SetForceEnabled (host) liga/desliga a força
    bool  tripped = false;         ///< desarme latched por sobrecorrente

    /// Um passo da malha: lê encoder+corrente → pipeline M5 → slew → comanda o motor.
    /// Retorna o torque comandado (Nm) — útil p/ testar.
    float step(int32_t hostForce, IEncoder& enc, ICurrentSense& sense, IMotor& motor) {
        if (!enabled || tripped) {
            motor.disable();
            _prev = 0.0f;
            return 0.0f;
        }

        float ia, ib, ic;
        sense.readPhaseCurrents(ia, ib, ic);
        if (overCurrent(ia, ib, ic, currentLimitA)) {
            tripped = true;       // latched: só religa via rearme explícito (rearm())
            motor.disable();
            _prev = 0.0f;
            return 0.0f;
        }

        const float target = computeTorque(hostForce, enc.positionRad(), enc.velocityRadPerSec(),
                                           force, effect, endstop);
        const float t = slewLimit(target, _prev, maxSlewNmPerStep);
        motor.setTorque(t);
        _prev = t;
        return t;
    }

    /// Rearma após um desarme por sobrecorrente (ex.: comando do host depois de investigar).
    void rearm() { tripped = false; _prev = 0.0f; }

private:
    float _prev = 0.0f;   ///< torque do passo anterior (para o slew-rate)
};

}  // namespace drivelab
