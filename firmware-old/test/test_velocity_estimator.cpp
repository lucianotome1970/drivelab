// ============================================================================
//  DriveLab Firmware
//  test_velocity_estimator.cpp — Teste de HOST do estágio low-pass da
//  VelocityEstimator (lib/brain/filters.h) usado pelo HAL do motor (M5,
//  Task 3, lib/base_motor/motor_hal.h — FocEncoder::velocityRadPerSec()):
//  entrada ruidosa -> saída com variância bem menor (velocidade suavizada).
//
//  Nota sobre a API exercitada: FocEncoder chama diretamente `m_velEst.lpf.
//  process(vel)` (só o estágio Biquad low-pass PÚBLICO da VelocityEstimator),
//  NÃO `m_velEst.update(vel, dt)`. `update()` faz diferença finita de
//  POSIÇÃO + low-pass; alimentá-lo com uma velocidade já diferenciada pelo
//  SimpleFOC (encoder.getVelocity()) calcularia aceleração/jerk filtrado,
//  não velocidade suave — o oposto do que este passo pede. Por isso o teste
//  abaixo exercita exatamente `lpf.process(...)`, o caminho real usado pelo
//  HAL. Roda sem placa: firmware-base/test/run.sh.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include "../lib/brain/filters.h"

#include <cstdio>
#include <random>

static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

int main()
{
    using namespace drivelab;

    const float fs = 1000.0f; // Hz — mesma ordem de grandeza do loop FOC
    VelocityEstimator ve;
    ve.lpf = makeLowPass(20.0f, fs, 0.707f); // corta ruído acima de 20 Hz

    // PRNG determinístico (semente fixa) — reprodutível entre execuções/CI.
    std::mt19937 rng(42);
    std::normal_distribution<float> noise(0.0f, 2.0f); // ruído gaussiano, desvio 2.0

    const int total = 2000;
    const int warmup = 300; // descarta transiente do filtro antes de medir variância

    double inSum = 0.0, inSumSq = 0.0;
    double outSum = 0.0, outSumSq = 0.0;
    int n = 0;

    for (int i = 0; i < total; ++i)
    {
        const float x = 5.0f + noise(rng); // "velocidade" ruidosa em torno de 5 rad/s
        const float y = ve.lpf.process(x);

        if (i >= warmup)
        {
            inSum += x; inSumSq += static_cast<double>(x) * x;
            outSum += y; outSumSq += static_cast<double>(y) * y;
            ++n;
        }
    }

    const double inMean = inSum / n, outMean = outSum / n;
    const double inVar = inSumSq / n - inMean * inMean;
    const double outVar = outSumSq / n - outMean * outMean;

    CHECK(n > 0);
    CHECK(outVar < 0.3 * inVar);          // suavização forte (>3x menos variância)
    CHECK(std::fabs(outMean - 5.0) < 0.5); // DC (velocidade real) preservado

    std::printf("test_velocity_estimator: %d checks, %d fails (inVar=%.3f outVar=%.3f)\n",
                g_checks, g_fails, inVar, outVar);
    return g_fails == 0 ? 0 : 1;
}
