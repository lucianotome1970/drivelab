// ============================================================================
//  DriveLab Firmware
//  cogging.h — Mapa de compensação de cogging (feed-forward por posição) + calibrador. Rumo ao topo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Cogging = ripple de torque do motor dependente da POSIÇÃO (ímãs × ranhuras). É o que faz forças
// pequenas parecerem "granuladas" a baixa velocidade — um dos maiores separadores entre DD bom e
// DD topo. Solução: medir o ripple em função da posição (girando devagar), guardar um MAPA por-motor
// e somar o NEGATIVO como feed-forward a cada tick. A tabela+interpolação e a matemática de calibração
// são puras/testáveis no host; a coleta das amostras é o único passo que precisa de bancada.
#pragma once

#include <cmath>

namespace drivelab {

/// Tabela de compensação (Nm) por posição mecânica, N bins em [0, 2π), com interpolação e wrap-around.
template <int N>
struct CoggingMap {
    float table[N] = { 0.0f };

    /// Compensação interpolada (feed-forward a SOMAR ao torque) para a posição atual.
    float compensation(float positionRad) const {
        constexpr float twoPi = 6.28318530718f;
        float p = std::fmod(positionRad, twoPi);
        if (p < 0.0f) p += twoPi;
        const float x = p / twoPi * static_cast<float>(N);
        int i0 = static_cast<int>(x);
        if (i0 >= N) i0 = N - 1;                 // guarda numérica
        const int   i1   = (i0 + 1) % N;         // wrap: o último bin liga no primeiro
        const float frac = x - static_cast<float>(i0);
        return table[i0] * (1.0f - frac) + table[i1] * frac;
    }

    void clear() { for (int i = 0; i < N; ++i) table[i] = 0.0f; }
};

/// Constrói o mapa a partir de amostras (posição, torque medido) coletadas girando o motor devagar:
/// média por bin → remove o offset DC (carga/atrito, NÃO é cogging) → inverte o sinal (compensação).
template <int N>
class CoggingCalibrator {
public:
    void addSample(float positionRad, float measuredTorque) {
        constexpr float twoPi = 6.28318530718f;
        float p = std::fmod(positionRad, twoPi);
        if (p < 0.0f) p += twoPi;
        int i = static_cast<int>(p / twoPi * static_cast<float>(N));
        if (i >= N) i = N - 1;
        _sum[i] += measuredTorque;
        _count[i] += 1;
    }

    /// Finaliza a calibração preenchendo out.table com a compensação (−ripple, DC removido).
    void finish(CoggingMap<N>& out) const {
        float avg[N];
        float mean = 0.0f;
        int used = 0;
        for (int i = 0; i < N; ++i) {
            avg[i] = _count[i] > 0 ? _sum[i] / static_cast<float>(_count[i]) : 0.0f;
            if (_count[i] > 0) { mean += avg[i]; ++used; }
        }
        if (used > 0) mean /= static_cast<float>(used);
        for (int i = 0; i < N; ++i)
            out.table[i] = -(avg[i] - mean);     // tira o DC e inverte → cancela o ripple
    }

    void reset() { for (int i = 0; i < N; ++i) { _sum[i] = 0.0f; _count[i] = 0; } }

private:
    float _sum[N]   = { 0.0f };
    int   _count[N] = { 0 };
};

}  // namespace drivelab
