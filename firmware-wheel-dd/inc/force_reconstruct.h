// ============================================================================
//  DriveLab Firmware
//  force_reconstruct.h — Reconstrução da força do jogo (interpolação + LPF) p/ saída contínua e suave.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
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
    int   steps         = 8;    ///< janela FIXA de interpolação (ticks/atualização). >0 = fixa (≤1 = ZOH); ≤0 = ADAPTATIVA
    int   fallbackSteps = 8;    ///< modo adaptativo: janela usada até haver a 1ª medição do intervalo do jogo
    float lpfAlpha      = 0.0f; ///< low-pass extra: 0 = desligado; (0,1) = alpha (menor = mais suave); 1 = passa direto
};

/// Reconstrói a força do jogo (discreta) numa saída suave por tick. Puro/testável.
///
/// Modo ADAPTATIVO (cfg.steps ≤ 0): em vez de assumir 60 Hz fixo, MEDE o intervalo real entre reports do
/// jogo (contando ticks do laço entre setTarget()s) e interpola exatamente sobre ele — casa sozinho com
/// ACC (~60 Hz) ou iRacing (~360 Hz), minimizando latência sem reintroduzir "degrau". Suaviza o intervalo
/// medido por EMA (contra jitter) e usa cfg.fallbackSteps até a 1ª medição existir.
class ForceReconstructor {
public:
    ReconstructConfig cfg;

    /// Nova amostra da força do jogo (chamar na taxa do jogo, quando um FFB report chega).
    void setTarget(float target) {
        const int window = resolveWindow();
        _ticksSinceTarget = 0;                 // reinicia a medição do intervalo do jogo
        _target = target;
        if (window > 1) {
            _stepInc   = (_target - _current) / static_cast<float>(window);
            _remaining = window;
        } else {                       // ZOH: assume o alvo na hora
            _current = _target; _stepInc = 0.0f; _remaining = 0;
        }
    }

    /// Um tick do laço rápido → força reconstruída (interpolada + suavizada).
    float tick() {
        ++_ticksSinceTarget;   // conta ticks desde o último report → mede o intervalo do jogo (modo adaptativo)
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
    /// Janela de interpolação em uso (p/ telemetria/testes): fixa ou a média adaptativa arredondada.
    int   window() const { return _lastWindow; }
    void  reset() {
        _target = _current = _filtered = 0.0f; _stepInc = 0.0f; _remaining = 0;
        _avgInterval = 0.0f; _ticksSinceTarget = 0; _lastWindow = 0;
    }

private:
    // Decide a janela desta atualização: fixa (cfg.steps>0) ou adaptativa (mede o intervalo).
    int resolveWindow() {
        int w;
        if (cfg.steps > 0) {
            w = cfg.steps;                                   // janela fixa configurada
        } else if (_avgInterval <= 0.0f) {
            _avgInterval = cfg.fallbackSteps > 0 ? static_cast<float>(cfg.fallbackSteps) : 1.0f; // 1ª vez: fallback
            w = static_cast<int>(_avgInterval + 0.5f);
        } else {
            // intervalo medido (ticks entre reports), limitado e suavizado por EMA
            float sample = static_cast<float>(_ticksSinceTarget);
            if (sample > static_cast<float>(kMaxWindow)) sample = static_cast<float>(kMaxWindow);
            _avgInterval += kIntervalEma * (sample - _avgInterval);
            w = static_cast<int>(_avgInterval + 0.5f);
        }
        if (w < 1) w = 1;
        if (w > kMaxWindow) w = kMaxWindow;                  // teto de latência
        _lastWindow = w;
        return w;
    }

    static constexpr float kIntervalEma = 0.25f;   // suavização do intervalo medido (adaptativo)
    static constexpr int   kMaxWindow   = 512;     // teto da janela (latência) e clamp do intervalo medido

    float _target = 0.0f, _current = 0.0f, _filtered = 0.0f, _stepInc = 0.0f;
    int   _remaining = 0;
    float _avgInterval = 0.0f;   // média (EMA) do intervalo do jogo em ticks — modo adaptativo
    int   _ticksSinceTarget = 0; // ticks desde o último setTarget() (mede o intervalo)
    int   _lastWindow = 0;       // última janela resolvida (telemetria)
};

}  // namespace drivelab
