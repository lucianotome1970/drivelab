// ============================================================================
//  DriveLab Firmware
//  test_telemetry_force.cpp — Teste de HOST da força aditiva de telemetria (setTelemetryForce) e do clip
//  meter agora FUNCIONAL (mede a demanda CRUA, pré-teto). Sem placa: firmware-base/test/run.sh.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include "ffb_engine.h"

#include <cstdio>

static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

using namespace drivelab;

static void configure(FfbEngine& e) {
    e.force.maxTorqueNm     = 2.5f;   // motor pediria até 2.5 Nm a 100%
    e.force.torqueLimitNm   = 1.0f;   // mas o teto de segurança corta em 1.0 Nm
    e.force.totalStrengthPct = 100.0f;
    e.force.linearity       = 100.0f; // resposta linear
    e.force.direction       = 1.0f;
}

int main()
{
    // --- força de telemetria além do teto → clip sobe ---
    {
        FfbEngine e; configure(e);
        e.setTelemetryForce(255.0f);          // demanda crua = 1*1*2.5 = 2.5 Nm > teto 1.0
        e.measureClipOnly(0.01f);             // sem girar o motor
        // clip instantâneo = (2.5-1.0)/2.5 = 0.6 → ~153/255
        CHECK(e.clipping() > 100);
        CHECK(e.telemetryForce() == 255.0f);
    }

    // --- força de telemetria dentro do teto → sem clip ---
    {
        FfbEngine e; configure(e);
        e.setTelemetryForce(50.0f);           // 50/255*2.5 = 0.49 Nm < 1.0 → não corta
        e.measureClipOnly(0.01f);
        CHECK(e.clipping() == 0);
    }

    // --- decaimento: zera a força e o clip cai a zero com o tempo ---
    {
        FfbEngine e; configure(e);
        e.setTelemetryForce(255.0f);
        e.measureClipOnly(0.01f);
        CHECK(e.clipping() > 0);
        e.setTelemetryForce(0.0f);
        for (int i = 0; i < 300; ++i) e.measureClipOnly(0.01f);
        CHECK(e.clipping() == 0);
    }

    std::printf("telemetry_force: %d checks, %d fails\n", g_checks, g_fails);
    return g_fails == 0 ? 0 : 1;
}
