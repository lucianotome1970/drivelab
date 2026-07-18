// ============================================================================
//  DriveLab Firmware
//  ffb_engine.h — Pipeline FFB completo num só step(): proteção→partida→reconstrução→força→cogging→filtro→segurança.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================
//
// A culminância do "cérebro": encadeia TODOS os blocos (M1/M2/M5 + reconstrução + cogging + filtros)
// numa única chamada por tick do laço, orquestrando via HAL. É o que o firmware chama no loop — a
// única coisa que muda entre a bancada e o teste é a implementação das interfaces. Ordem por tick:
//   proteção de potência → máquina de partida (gate/rampa) → sobrecorrente →
//   reconstrução da força → torque (força+efeitos+soft-stop) → cogging → filtro de saída → rampa → slew → teto duro.
#pragma once

#include "ffb_math.h"
#include "ffb_power.h"
#include "startup.h"
#include "force_reconstruct.h"
#include "cogging.h"
#include "filters.h"
#include "oscillation.h"
#include "hal.h"

namespace drivelab {

using CoggingTable = CoggingMap<128>;   ///< 128 bins/rev; tabela mora na flash (calibrada por-motor)

class FfbEngine {
public:
    // --- configuração (blocos expostos p/ ajuste direto) ---
    ForceConfig        force;
    EffectConfig       effect;
    EndstopConfig      endstop;
    ForceReconstructor reconstructor;      ///< reconstrução da força do jogo
    StartupSequencer   startup;            ///< máquina de partida (gate + rampa)
    PowerGuard         guard;              ///< brake resistor + falha de potência
    Biquad             outputFilter;       ///< notch/low-pass na saída (default passa-tudo)
    const CoggingTable* cogging = nullptr; ///< feed-forward de cogging (opcional)
    OscillationDetector oscGuard;          ///< anti-tremor ativo (desinfla a força se detectar limit-cycle)
    bool  oscGuardEnabled  = false;        ///< liga o detector de oscilação
    float currentLimitA    = 8.0f;
    float maxSlewNmPerStep  = 0.0f;
    bool  enableRequested   = false;       ///< SetForceEnabled (host)

    /// Força FFB do jogo (chamar quando chega um report; a reconstrução espalha entre os ticks).
    void setGameForce(float force255) { reconstructor.setTarget(force255); }

    /// Um tick do laço (dt em segundos). Retorna o torque comandado (Nm).
    float step(float dt, IEncoder& enc, ICurrentSense& cs,
               IPowerSense& pw, IBrakeResistor& br, IMotor& motor) {
        // 1) Proteção de potência: brake resistor + avaliação de falha (sobretensão/sobretemp).
        guard.step(pw, br);

        // 2) Máquina de partida: decide se/quanto a força flui (com inter-travamentos + rampa).
        const float maxTemp = pw.mosfetTempC() > pw.motorTempC() ? pw.mosfetTempC() : pw.motorTempC();
        StartupInputs in{ enableRequested, guard.faulted, pw.busVoltage(), maxTemp };
        startup.update(dt, in);

        if (!startup.forceEnabled()) {
            _prev = 0.0f;
            if (startup.state == MotorState::Aligning) {   // alinha o rotor open-loop
                const float a = startup.alignTorque();
                motor.setTorque(a);
                return a;
            }
            motor.disable();                                // Idle / Fault
            return 0.0f;
        }

        // 3) Sobrecorrente → desarma a proteção (latched) e desliga.
        float ia, ib, ic; cs.readPhaseCurrents(ia, ib, ic);
        if (overCurrent(ia, ib, ic, currentLimitA)) {
            guard.faulted = true; motor.disable(); _prev = 0.0f;
            return 0.0f;
        }

        // 4) Pipeline de força.
        const float hostF = reconstructor.tick();                       // força do jogo reconstruída
        const float pos = enc.positionRad(), vel = enc.velocityRadPerSec();
        float t = computeTorque(hostF, pos, vel, force, effect, endstop); // força+efeitos+soft-stop
        if (cogging) t += cogging->compensation(pos);                   // feed-forward de cogging
        t = outputFilter.process(t);                                    // notch/low-pass opcional
        if (oscGuardEnabled) t *= oscGuard.update(vel, dt);             // anti-tremor ativo (desinfla se oscilar)
        t *= startup.rampGain();                                        // rampa de subida (soft start)
        t = slewLimit(t, _prev, maxSlewNmPerStep);                      // limite de variação
        t = clampf(t, -force.torqueLimitNm, force.torqueLimitNm);       // TETO DURO sempre por último

        motor.setTorque(t);
        _prev = t;
        return t;
    }

private:
    float _prev = 0.0f;   ///< torque anterior (slew-rate)
};

}  // namespace drivelab
