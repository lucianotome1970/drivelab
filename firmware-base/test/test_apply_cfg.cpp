// ============================================================================
//  DriveLab Firmware
//  test_apply_cfg.cpp — Testes de HOST da ponte applyCfgToEngine (BaseCfg →
//  FfbEngine): endpoints de escala, casos default/auto e não-regressão dos
//  campos que ficam fora deste SP (cogging table). Roda sem placa nenhuma.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Roda sem placa nenhuma: firmware-base/test/run.sh

#include "../lib/base_shared/base_cfg.h"
#include "../lib/base_shared/apply_cfg.h"

#include <cstdio>
#include <cmath>

using drivelab::FfbEngine;
using drivelab::kSpringMaxNmPerRad;

// ----- micro-harness (mesmo padrão de test_base_cfg.cpp) -----
static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

static const float kLoopHz = 8000.0f;

int main() {
    // ----- defaults: identidade / desligado -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        FfbEngine e;
        applyCfgToEngine(c, e, kLoopHz);

        CHECK(std::fabs(e.outputFilter.b0 - 1.0f) < 1e-6f);
        CHECK(std::fabs(e.outputFilter.b1) < 1e-6f);
        CHECK(std::fabs(e.outputFilter.b2) < 1e-6f);
        CHECK(std::fabs(e.outputFilter.a1) < 1e-6f);
        CHECK(std::fabs(e.outputFilter.a2) < 1e-6f);
        CHECK(e.oscGuardEnabled == false);
        CHECK(e.cogging == nullptr);
        CHECK(e.force.linearity == 1.0f);
        CHECK(e.reconstructor.cfg.lpfAlpha == 0.0f);
    }

    // ----- totalStrength passa direto -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        c.totalStrength = 100;
        FfbEngine e;
        applyCfgToEngine(c, e, kLoopHz);
        CHECK(e.force.totalStrengthPct == 100.0f);
    }

    // ----- springStrength: 100 -> max, 0 -> 0 -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        c.springStrength = 100;
        FfbEngine e;
        applyCfgToEngine(c, e, kLoopHz);
        CHECK(std::fabs(e.effect.springNmPerRad - kSpringMaxNmPerRad) < 1e-6f);
    }
    {
        BaseCfg c;
        baseSeedDefaults(c);
        c.springStrength = 0;
        FfbEngine e;
        applyCfgToEngine(c, e, kLoopHz);
        CHECK(e.effect.springNmPerRad == 0.0f);
    }

    // ----- linearity: 200 -> 2.0, 50 -> 0.5 -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        c.linearity = 200;
        FfbEngine e;
        applyCfgToEngine(c, e, kLoopHz);
        CHECK(std::fabs(e.force.linearity - 2.0f) < 1e-6f);
    }
    {
        BaseCfg c;
        baseSeedDefaults(c);
        c.linearity = 50;
        FfbEngine e;
        applyCfgToEngine(c, e, kLoopHz);
        CHECK(std::fabs(e.force.linearity - 0.5f) < 1e-6f);
    }

    // ----- outputFilterHz > 0 -> filtro real (b0 != 1) -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        c.outputFilterHz = 200;
        FfbEngine e;
        applyCfgToEngine(c, e, kLoopHz);
        CHECK(std::fabs(e.outputFilter.b0 - 1.0f) > 1e-6f);
    }

    // ----- coggingEnable=1: sem tabela de bancada, cogging continua nullptr -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        c.coggingEnable = 1;
        FfbEngine e;
        applyCfgToEngine(c, e, kLoopHz);
        CHECK(e.cogging == nullptr);
    }

    // ----- reconstructionSteps: 0 = auto (round(loopHz/kGameForceHz)), 8 = direto -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        c.reconstructionSteps = 0;
        FfbEngine e;
        applyCfgToEngine(c, e, kLoopHz);
        CHECK(e.reconstructor.cfg.steps == 133); // round(8000/60) = 133
    }
    {
        BaseCfg c;
        baseSeedDefaults(c);
        c.reconstructionSteps = 8;
        FfbEngine e;
        applyCfgToEngine(c, e, kLoopHz);
        CHECK(e.reconstructor.cfg.steps == 8);
    }

    std::printf("apply_cfg: %d checks, %d fails\n", g_checks, g_fails);
    return g_fails ? 1 : 0;
}
