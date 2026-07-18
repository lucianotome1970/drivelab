// ============================================================================
//  DriveLab Firmware
//  startup.h — Sequência de partida segura do motor (Idle→Alinhamento→Rodando→Falha) + rampa (M1).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================
//
// M1 (motor open-loop) = a PRIMEIRA vez que o motor energiza. O perigoso não é o giro em si
// (o SimpleFOC gera o ângulo/FOC), e sim a SEQUÊNCIA: só armar com os inter-travamentos ok
// (tensão na faixa, temperatura ok, sem falha da proteção), alinhar o rotor open-loop com
// torque baixo por um tempo, e só então liberar a força — com uma RAMPA de subida (nada de
// solavanco). Falha tem prioridade em qualquer estado. Tudo puro/testável no host.
#pragma once

#include "ffb_math.h"   // clampf

namespace drivelab {

enum class MotorState { Idle, Aligning, Running, Fault };

struct StartupConfig {
    float busMinV       = 20.0f;  ///< não arma abaixo disso
    float busMaxV       = 30.0f;  ///< nem acima (sistema 24V)
    float tempMaxC      = 80.0f;
    float alignSeconds  = 0.5f;   ///< tempo de alinhamento open-loop
    float alignTorqueNm = 0.3f;   ///< torque baixo de alinhamento
    float rampSeconds   = 0.5f;   ///< subida da força ao entrar em Running (0 = imediato)
};

/// Entradas por passo (o laço principal preenche a partir do host + da proteção + sense).
struct StartupInputs {
    bool  enableRequested = false;  ///< host pediu força (SetForceEnabled)
    bool  guardFaulted    = false;  ///< PowerGuard.faulted (sobretensão/sobretemp)
    float busVoltage      = 0.0f;
    float maxTempC        = 0.0f;   ///< maior entre FET/motor
};

/// Máquina de estados da partida. Não toca o motor — decide se/quanto liberar; o laço aplica.
class StartupSequencer {
public:
    StartupConfig cfg;
    MotorState state = MotorState::Idle;

    bool interlocksOk(const StartupInputs& in) const {
        return !in.guardFaulted
            && in.busVoltage >= cfg.busMinV && in.busVoltage <= cfg.busMaxV
            && in.maxTempC   <= cfg.tempMaxC;
    }

    /// A força FFB só flui em Running.
    bool  forceEnabled() const { return state == MotorState::Running; }
    /// Ganho de rampa [0..1] aplicado à força em Running (0 fora dele).
    float rampGain() const { return _ramp; }
    /// Torque de alinhamento (Nm) — só durante Aligning.
    float alignTorque() const { return state == MotorState::Aligning ? cfg.alignTorqueNm : 0.0f; }

    /// Avança um passo (dt em segundos).
    void update(float dt, const StartupInputs& in) {
        // Falha tem prioridade absoluta (menos se já está em Fault).
        if (in.guardFaulted && state != MotorState::Fault) {
            state = MotorState::Fault; _timer = 0.0f; _ramp = 0.0f; return;
        }
        switch (state) {
            case MotorState::Idle:
                _ramp = 0.0f;
                if (in.enableRequested && interlocksOk(in)) { state = MotorState::Aligning; _timer = 0.0f; }
                break;
            case MotorState::Aligning:
                if (!in.enableRequested) { state = MotorState::Idle; break; }
                _timer += dt;
                if (_timer >= cfg.alignSeconds) { state = MotorState::Running; _ramp = 0.0f; }
                break;
            case MotorState::Running:
                if (!in.enableRequested) { state = MotorState::Idle; _ramp = 0.0f; break; }
                _ramp = cfg.rampSeconds > 0.0f ? clampf(_ramp + dt / cfg.rampSeconds, 0.0f, 1.0f) : 1.0f;
                break;
            case MotorState::Fault:
                _ramp = 0.0f;   // sai só via clearFault() (e volta a Fault no próx. tick se ainda faltar)
                break;
        }
    }

    /// Tenta sair da falha (o chamador garante que a causa foi investigada); re-entra se persistir.
    void clearFault() { if (state == MotorState::Fault) state = MotorState::Idle; }

private:
    float _timer = 0.0f;   ///< tempo no Aligning
    float _ramp  = 0.0f;   ///< ganho de rampa em Running
};

}  // namespace drivelab
