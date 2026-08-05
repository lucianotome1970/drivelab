// ============================================================================
//  DriveLab Firmware
//  oscillation.h — Detector de oscilação (limit-cycle) → reduz a força sozinho. Anti-tremor ativo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Rede de segurança para o "tremor": mesmo com damper, uma combinação ruim de latência/ganho pode
// entrar em oscilação sustentada. Este detector vê cruzamentos de sinal RÁPIDOS e de amplitude
// significativa (um limit-cycle) e **reduz um ganho de saída** enquanto durar — recuperando devagar
// quando acalma. É o que faz a base "desinflar" a força sozinha em vez de sacudir o usuário.
#pragma once

#include "ffb_math.h"   // clampf

namespace drivelab {

struct OscillationDetector {
    float crossHz       = 8.0f;   ///< cruzamentos acima disso (Hz) = suspeito de oscilação
    float minAmplitude  = 1.0f;   ///< rad/s de amplitude p/ contar um cruzamento (ignora ruído)
    float holdSeconds   = 0.3f;   ///< mantém "oscilando" por este tempo após o último cruzamento rápido
    float attackPerSec  = 3.0f;   ///< quão rápido reduz o ganho ao detectar
    float releasePerSec = 0.5f;   ///< quão rápido recupera quando acalma
    float floorGain     = 0.2f;   ///< piso do ganho (nunca zera — mantém controle)
    float gain          = 1.0f;   ///< SAÍDA: multiplicador de força [floor..1]

    /// Alimente com um sinal (ex.: velocidade) + dt. Retorna o ganho a aplicar na força.
    float update(float signal, float dt) {
        _t += dt;
        const int sign = signal > minAmplitude ? 1 : (signal < -minAmplitude ? -1 : 0);
        if (sign != 0 && _lastSign != 0 && sign != _lastSign) {   // cruzou com amplitude real
            const float half = _t - _lastCrossT;
            if (half > 0.0f && 0.5f / half > crossHz) _lastFast = _t;   // cruzamento RÁPIDO
            _lastCrossT = _t;
        }
        if (sign != 0) _lastSign = sign;

        const bool oscillating = (_t - _lastFast) < holdSeconds;
        gain += (oscillating ? -attackPerSec : releasePerSec) * dt;
        gain = clampf(gain, floorGain, 1.0f);
        return gain;
    }

    void reset() { _t = 0.0f; _lastCrossT = 0.0f; _lastFast = -1e9f; _lastSign = 0; gain = 1.0f; }

private:
    float _t = 0.0f, _lastCrossT = 0.0f, _lastFast = -1e9f;
    int   _lastSign = 0;
};

}  // namespace drivelab
