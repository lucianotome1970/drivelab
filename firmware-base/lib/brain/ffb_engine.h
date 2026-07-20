// ============================================================================
//  DriveLab Firmware
//  ffb_engine.h — Pipeline FFB completo num só step(): proteção→partida→reconstrução→força→cogging→filtro→segurança.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
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
#include "effect_manager.h"

namespace drivelab {

using CoggingTable = CoggingMap<128>;   ///< 128 bins/rev; tabela mora na flash (calibrada por-motor)

/// Ganho do watchdog de sinal FFB perdido: 1.0 enquanto o jogo reporta dentro do
/// timeout; rampa linear 1→0 ao longo de decayMs após o timeout; 0.0 depois disso
/// (força zerada — segurança). Pura, host-testável (sem motor/hardware).
inline float ffbWatchdogGain(uint32_t dtSilentMs, uint32_t timeoutMs, uint32_t decayMs) {
    if (dtSilentMs <= timeoutMs) return 1.0f;
    uint32_t over = dtSilentMs - timeoutMs;
    if (over >= decayMs) return 0.0f;
    return 1.0f - (float)over / (float)decayMs;
}

class FfbEngine {
public:
    static constexpr uint32_t kFfbTimeoutMs = 500;  ///< silêncio tolerado antes de começar a decair (ms)
    static constexpr uint32_t kFfbDecayMs   = 300;  ///< duração da rampa de decaimento até zero (ms)

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
    EffectManager       effects;           ///< banco de efeitos PID do jogo (Sub-projeto 2) — soma aditiva no hostF
    bool  oscGuardEnabled  = false;        ///< liga o detector de oscilação
    float currentLimitA    = 8.0f;
    float maxSlewNmPerStep  = 0.0f;
    bool  enableRequested   = false;       ///< SetForceEnabled (host)

    /// Força FFB do jogo (chamar quando chega um report; a reconstrução espalha entre os ticks).
    void setGameForce(float force255) { reconstructor.setTarget(force255); }

    /// Clock acumulado do engine (ms), avançado a cada step() por dt — MESMA
    /// base de tempo usada por effects.handleReport()/computeForce() (não
    /// misturar com millis() direto: precisam compartilhar um único relógio,
    /// senão phase/expiry dos efeitos ficam incoerentes).
    uint32_t nowMs() const { return m_nowMs; }

    /// Chamar a cada report de FFB do host (m5) — reseta o relógio do watchdog.
    void notifyFfbActivity() { m_lastFfbMs = m_nowMs; }

    /// Um tick do laço (dt em segundos). Retorna o torque comandado (Nm).
    float step(float dt, IEncoder& enc, ICurrentSense& cs,
               IPowerSense& pw, IBrakeResistor& br, IMotor& motor) {
        // Avança o clock acumulado do engine ANTES de qualquer uso — é a
        // base de tempo compartilhada com effects.handleReport() (chamado
        // pelo callback USB via nowMs()).
        m_nowMs += (uint32_t)(dt * 1000.0f + 0.5f);

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
        float hostF = reconstructor.tick();                             // força do jogo reconstruída
        const float pos = enc.positionRad(), vel = enc.velocityRadPerSec();
        hostF += effects.computeForce(pos, vel, m_nowMs);                // soma aditiva dos efeitos PID (Constant já vem pelo reconstructor, pulado lá dentro)
        hostF *= ffbWatchdogGain(m_nowMs - m_lastFfbMs, kFfbTimeoutMs, kFfbDecayMs); // sinal perdido → decai a zero
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
    float _prev = 0.0f;      ///< torque anterior (slew-rate)
    uint32_t m_nowMs = 0;    ///< clock acumulado do engine (ms) — ver nowMs()
    uint32_t m_lastFfbMs = 0; ///< timestamp do último report FFB (m_nowMs) — ver notifyFfbActivity()
};

}  // namespace drivelab
