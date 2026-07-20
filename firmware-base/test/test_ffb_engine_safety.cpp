// ============================================================================
//  DriveLab Firmware
//  test_ffb_engine_safety.cpp — Testes de HOST do watchdog de sinal FFB
//  perdido (ffbWatchdogGain): ganho 1.0 até o timeout, rampa linear até
//  zero ao longo do decay, 0.0 depois — sem placa, sem motor.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Roda sem placa nenhuma: firmware-base/test/run.sh

#include "../lib/brain/ffb_engine.h"

#include <cmath>
#include <cstdio>

using drivelab::ffbWatchdogGain;

// ----- micro-harness (mesmo padrão de test_effect_manager.cpp) -----
static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

int main() {
    // ---- dentro do timeout: ganho pleno ----
    CHECK(ffbWatchdogGain(0, 500, 300) == 1.0f);
    CHECK(ffbWatchdogGain(500, 500, 300) == 1.0f);

    // ---- na rampa: decaimento linear ----
    CHECK(std::fabs(ffbWatchdogGain(650, 500, 300) - 0.5f) < 0.01f);

    // ---- além do decay: zero ----
    CHECK(ffbWatchdogGain(800, 500, 300) == 0.0f);
    CHECK(ffbWatchdogGain(2000, 500, 300) == 0.0f);

    if (g_fails == 0) std::printf("OK: %d checks\n", g_checks);
    return g_fails ? 1 : 0;
}
