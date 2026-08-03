// firmware-wheel-dd — biquad_lp.h — filtro lowpass biquad (RBJ) p/ suavizar os efeitos de
// condição do FFB (Damper/Inertia/Friction), como o OpenFFBoard faz. Sem ele o Inertia (derivada
// dupla) e o Damper (velocidade ruidosa) viram PICOS de torque a 1kHz → movimento inesperado do
// FFB (ex.: T1 de Monza) e até desarme por INVALID_ESTIMATE (o pico chuta o volante rápido).
//
// Fórmulas do RBJ Audio-EQ Cookbook (DOMÍNIO PÚBLICO) — reimplementação PRÓPRIA, não é cópia do
// código GPL do OpenFFBoard; só a MESMA classe de filtro. Direct Form II Transposed.
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#pragma once
#include <cmath>

namespace drivelab {

class BiquadLP {
public:
    // fc_hz: corte; q: fator de qualidade; fs_hz: taxa de amostragem (1000 do laço FFB).
    void configure(float fc_hz, float q, float fs_hz) {
        if (fc_hz <= 0.0f || fs_hz <= 0.0f || fc_hz >= fs_hz * 0.5f || q <= 0.0f) {
            m_bypass = true; return;
        }
        m_bypass = false;
        const float w0 = 2.0f * 3.14159265358979f * fc_hz / fs_hz;
        const float cw = std::cos(w0), sw = std::sin(w0);
        const float alpha = sw / (2.0f * q);
        const float a0 = 1.0f + alpha;
        m_b0 = (1.0f - cw) * 0.5f / a0;
        m_b1 = (1.0f - cw) / a0;
        m_b2 = m_b0;
        m_a1 = (-2.0f * cw) / a0;
        m_a2 = (1.0f - alpha) / a0;
    }

    float process(float x) {
        if (m_bypass) return x;
        const float y = m_b0 * x + m_z1;
        m_z1 = m_b1 * x - m_a1 * y + m_z2;
        m_z2 = m_b2 * x - m_a2 * y;
        return y;
    }

    void reset() { m_z1 = 0.0f; m_z2 = 0.0f; }

private:
    float m_b0 = 1.0f, m_b1 = 0.0f, m_b2 = 0.0f, m_a1 = 0.0f, m_a2 = 0.0f;
    float m_z1 = 0.0f, m_z2 = 0.0f;
    bool  m_bypass = true;
};

}  // namespace drivelab
