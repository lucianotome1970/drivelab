// ============================================================================
//  DriveLab Firmware
//  force_reconstruct.h — Reconstrução da força do jogo (interpolação + LPF) p/ saída contínua e suave.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================
//
// O jogo manda a força FFB em passos discretos e a baixa taxa (60–360 Hz), mas o laço de
// torque roda muito mais rápido (10–40 kHz). Segurar o último valor (zero-order hold) gera
// "degraus" — é o que dá a sensação notchy/granulada. A reconstrução ESPALHA cada atualização
// ao longo dos ticks do laço rápido (rampa linear) e, opcionalmente, passa um low-pass — virando
// os degraus numa saída contínua ("silky"). É o algoritmo que separa DD bom de DD top; aqui ele
// é puro e MENSURÁVEL no host (comparar suavidade/latência antes de gravar).
#pragma once

#include "ffb_math.h"   // clampf

namespace drivelab {

struct ReconstructConfig {
    int   steps    = 8;      ///< ticks do laço rápido por atualização do jogo (janela de interpolação; ≤1 = ZOH)
    float lpfAlpha = 0.0f;   ///< low-pass extra: 0 = desligado; (0,1) = alpha (menor = mais suave); 1 = passa direto
};

/// Reconstrói a força do jogo (discreta) numa saída suave por tick. Puro/testável.
class ForceReconstructor {
public:
    ReconstructConfig cfg;

    /// Nova amostra da força do jogo (chamar na taxa do jogo, quando um FFB report chega).
    void setTarget(float target) {
        _target = target;
        if (cfg.steps > 1) {
            _stepInc   = (_target - _current) / static_cast<float>(cfg.steps);
            _remaining = cfg.steps;
        } else {                       // ZOH: assume o alvo na hora
            _current = _target; _stepInc = 0.0f; _remaining = 0;
        }
    }

    /// Um tick do laço rápido → força reconstruída (interpolada + suavizada).
    float tick() {
        if (_remaining > 0) {
            _current += _stepInc;
            if (--_remaining == 0) _current = _target;   // fecha exato no alvo (sem drift de float)
        }
        float out = _current;
        if (cfg.lpfAlpha > 0.0f && cfg.lpfAlpha < 1.0f) {
            _filtered += cfg.lpfAlpha * (out - _filtered);
            out = _filtered;
        }
        return out;
    }

    float current() const { return _current; }
    void  reset() { _target = _current = _filtered = 0.0f; _stepInc = 0.0f; _remaining = 0; }

private:
    float _target = 0.0f, _current = 0.0f, _filtered = 0.0f, _stepInc = 0.0f;
    int   _remaining = 0;
};

}  // namespace drivelab
