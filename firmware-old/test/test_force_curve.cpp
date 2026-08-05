// ============================================================================
//  DriveLab Firmware
//  test_force_curve.cpp — Teste de HOST da curva de resposta por pontos (ForceCurve/applyForceCurve):
//  identidade no default, interpolação entre pontos, simetria de sinal e saturação. Sem placa: test/run.sh.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include "../lib/brain/ffb_math.h"

#include <cstdio>
#include <cmath>

static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)
#define NEAR(a, b) (std::fabs((a) - (b)) < 1e-4f)

using drivelab::ForceCurve;
using drivelab::applyForceCurve;

int main()
{
    // ---- default é linear: identidade (quem não mexer não sente diferença) ----
    {
        ForceCurve c;
        CHECK(c.isLinear());
        CHECK(NEAR(applyForceCurve(0.00f, c), 0.00f));
        CHECK(NEAR(applyForceCurve(0.37f, c), 0.37f));
        CHECK(NEAR(applyForceCurve(1.00f, c), 1.00f));
        CHECK(NEAR(applyForceCurve(-0.62f, c), -0.62f));
    }

    // ---- curva "suave no leve" (abaixo da diagonal): pontos batem exatamente ----
    {
        ForceCurve c; c.p[0] = 0; c.p[1] = 10; c.p[2] = 30; c.p[3] = 60; c.p[4] = 100;
        CHECK(!c.isLinear());
        CHECK(NEAR(applyForceCurve(0.00f, c), 0.00f));   // ponto 0
        CHECK(NEAR(applyForceCurve(0.25f, c), 0.10f));   // ponto 1
        CHECK(NEAR(applyForceCurve(0.50f, c), 0.30f));   // ponto 2
        CHECK(NEAR(applyForceCurve(0.75f, c), 0.60f));   // ponto 3
        CHECK(NEAR(applyForceCurve(1.00f, c), 1.00f));   // ponto 4

        // interpolação linear no meio do 1º segmento (0→0.25 mapeia 0→0.10)
        CHECK(NEAR(applyForceCurve(0.125f, c), 0.05f));
        // meio do último segmento (0.75→1.0 mapeia 0.60→1.00)
        CHECK(NEAR(applyForceCurve(0.875f, c), 0.80f));
    }

    // ---- simetria: o sinal é preservado (FFB é bidirecional) ----
    {
        ForceCurve c; c.p[0] = 0; c.p[1] = 10; c.p[2] = 30; c.p[3] = 60; c.p[4] = 100;
        CHECK(NEAR(applyForceCurve(-0.50f, c), -0.30f));
        CHECK(NEAR(applyForceCurve(-1.00f, c), -1.00f));
    }

    // ---- entrada além de ±1 satura (não extrapola a curva) ----
    {
        ForceCurve c; c.p[0] = 0; c.p[1] = 10; c.p[2] = 30; c.p[3] = 60; c.p[4] = 100;
        CHECK(NEAR(applyForceCurve(1.8f, c), 1.00f));
        CHECK(NEAR(applyForceCurve(-1.8f, c), -1.00f));
    }

    // ---- curva pode LIMITAR o topo (ex.: teto de 80%) ----
    {
        ForceCurve c; c.p[0] = 0; c.p[1] = 20; c.p[2] = 40; c.p[3] = 60; c.p[4] = 80;
        CHECK(NEAR(applyForceCurve(1.0f, c), 0.80f));
    }

    std::printf("force_curve: %d checks, %d fails\n", g_checks, g_fails);
    return g_fails == 0 ? 0 : 1;
}
