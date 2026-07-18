// ============================================================================
//  DriveLab Firmware
//  filters.h — Biquad genérico (low-pass / notch, RBJ) + estimador de velocidade. DSP do topo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================
//
// Primitiva de DSP reutilizável — um filtro IIR de 2ª ordem (biquad, Direct Form II transposto).
// Com ele montamos: LOW-PASS (suavizar força/velocidade), NOTCH (matar uma ressonância mecânica
// específica — o "anti-notch" que evita a oscilação da correia/eixo) e o ESTIMADOR de velocidade
// (diferença finita + low-pass), do qual dependem a qualidade do damper/inertia/friction.
// Coeficientes pelo cookbook de Robert Bristow-Johnson (RBJ). Puro/testável no host.
#pragma once

#include <cmath>

namespace drivelab {

/// Biquad (2ª ordem) Direct Form II transposto. a0 já normalizado para 1.
struct Biquad {
    float b0 = 1.0f, b1 = 0.0f, b2 = 0.0f, a1 = 0.0f, a2 = 0.0f;
    float z1 = 0.0f, z2 = 0.0f;   // estado

    float process(float x) {
        const float y = b0 * x + z1;
        z1 = b1 * x - a1 * y + z2;
        z2 = b2 * x - a2 * y;
        return y;
    }
    void reset() { z1 = 0.0f; z2 = 0.0f; }
};

/// Low-pass RBJ: corta acima de f0 (Hz) na taxa fs (Hz); q ~0.707 = Butterworth.
inline Biquad makeLowPass(float f0, float fs, float q) {
    const float w0 = 2.0f * 3.14159265359f * f0 / fs;
    const float cw = std::cos(w0), sw = std::sin(w0);
    const float alpha = sw / (2.0f * q);
    const float a0 = 1.0f + alpha;
    Biquad bq;
    bq.b0 = (1.0f - cw) * 0.5f / a0;
    bq.b1 = (1.0f - cw) / a0;
    bq.b2 = (1.0f - cw) * 0.5f / a0;
    bq.a1 = (-2.0f * cw) / a0;
    bq.a2 = (1.0f - alpha) / a0;
    return bq;
}

/// Notch RBJ: rejeita a banda em torno de f0 (Hz), passa o resto. q alto = notch estreito/fundo.
inline Biquad makeNotch(float f0, float fs, float q) {
    const float w0 = 2.0f * 3.14159265359f * f0 / fs;
    const float cw = std::cos(w0), sw = std::sin(w0);
    const float alpha = sw / (2.0f * q);
    const float a0 = 1.0f + alpha;
    Biquad bq;
    bq.b0 = 1.0f / a0;
    bq.b1 = (-2.0f * cw) / a0;
    bq.b2 = 1.0f / a0;
    bq.a1 = (-2.0f * cw) / a0;
    bq.a2 = (1.0f - alpha) / a0;
    return bq;
}

/// Estimador de velocidade angular: diferença finita da posição + low-pass (menos ruído no damper).
/// Configure `lpf` com makeLowPass(...); por padrão é passa-tudo (velocidade crua).
class VelocityEstimator {
public:
    Biquad lpf;

    float update(float positionRad, float dt) {
        const float raw = dt > 0.0f ? (positionRad - _prev) / dt : 0.0f;
        _prev = positionRad;
        return lpf.process(raw);
    }
    void reset(float pos = 0.0f) { _prev = pos; lpf.reset(); }

private:
    float _prev = 0.0f;
};

/// Interpolação de encoder: o encoder dá posição QUANTIZADA (degraus de 2π/CPR). Entre um degrau e
/// o próximo, extrapola pela velocidade → posição mais fina/suave (melhora damper/inertia, sobretudo
/// em encoder de baixa resolução). Ao chegar um degrau novo, ancora nele e zera o acumulador.
class EncoderInterpolator {
public:
    float update(float rawQuantized, float velRadPerSec, float dt) {
        if (rawQuantized != _lastRaw) { _base = rawQuantized; _acc = 0.0f; _lastRaw = rawQuantized; }
        else _acc += dt;
        return _base + velRadPerSec * _acc;   // extrapola dentro do degrau
    }
    void reset() { _lastRaw = 0.0f; _base = 0.0f; _acc = 0.0f; }

private:
    float _lastRaw = 0.0f, _base = 0.0f, _acc = 0.0f;
};

}  // namespace drivelab
