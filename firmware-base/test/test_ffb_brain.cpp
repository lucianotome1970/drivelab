// ============================================================================
//  DriveLab Firmware
//  test_ffb_brain.cpp — Testes de HOST do cérebro FFB (força→torque, soft-stop, segurança).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================
//
// "HIL simplificado": compila o cérebro portável (lib/brain) num binário do PC, com o
// hardware substituído por mocks (valores em variáveis). Roda sem placa nenhuma:
//     firmware-base/test/run.sh
// Prova a correção da matemática força→torque e das proteções, e demonstra o padrão de
// mock das interfaces de HAL — o mesmo que o firmware usará com SimpleFOC/ADC reais.

#include "ffb_controller.h"

#include <cstdio>

using namespace drivelab;

// ----- micro-harness -----
static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { ++g_checks; if (!(cond)) { \
    std::printf("  FAIL %s:%d  %s\n", __FILE__, __LINE__, #cond); ++g_fails; } } while (0)
static bool approx(float a, float b, float eps = 1e-4f) { float d = a - b; return d < eps && d > -eps; }

// ----- mocks de HAL (o "HIL": valores injetados em variáveis) -----
struct MockEncoder : IEncoder {
    float pos = 0, vel = 0;
    float positionRad() override { return pos; }
    float velocityRadPerSec() override { return vel; }
};
struct MockSense : ICurrentSense {
    float a = 0, b = 0, c = 0;
    void readPhaseCurrents(float& ia, float& ib, float& ic) override { ia = a; ib = b; ic = c; }
};
struct MockMotor : IMotor {
    float lastTorque = 0; int setCalls = 0; int disableCalls = 0;
    void setTorque(float nm) override { lastTorque = nm; ++setCalls; }
    void disable() override { ++disableCalls; }
};

int main() {
    // 1) força→torque: escala linear + teto de segurança
    {
        ForceConfig fc; fc.totalStrengthPct = 100; fc.maxTorqueNm = 2.5f; fc.torqueLimitNm = 2.5f;
        CHECK(approx(forceToTorque(255, fc), 2.5f));      // força máxima → torque nominal
        CHECK(approx(forceToTorque(-255, fc), -2.5f));    // simétrico
        CHECK(approx(forceToTorque(0, fc), 0.0f));
        CHECK(approx(forceToTorque(128, fc), 128.0f / 255.0f * 2.5f));

        fc.totalStrengthPct = 50;                          // força total a 50% escala tudo
        CHECK(approx(forceToTorque(255, fc), 1.25f));

        fc.totalStrengthPct = 100; fc.maxTorqueNm = 5.0f;  // motor forte, mas teto 2.5 clampa
        CHECK(approx(forceToTorque(255, fc), 2.5f));
    }

    // 2) soft-stop (fim de curso): 0 dentro da faixa, mola empurrando de volta fora
    {
        EndstopConfig ec; ec.rangeRad = 1.0f; ec.stiffnessNm = 3.0f;
        CHECK(approx(endstopTorque(0.5f, ec), 0.0f));      // dentro → nada
        CHECK(approx(endstopTorque(1.5f, ec), -1.5f));     // além +: empurra p/ -
        CHECK(approx(endstopTorque(-1.5f, ec), 1.5f));     // além -: empurra p/ +
    }

    // 3) proteção por sobrecorrente
    {
        CHECK(!overCurrent(1, 1, 1, 8));
        CHECK(overCurrent(9, 0, 0, 8));
        CHECK(overCurrent(0, -9, 0, 8));
    }

    // 4) torque final: soft-stop pode dominar, mas o teto duro é sempre o último a mandar
    {
        ForceConfig fc; fc.totalStrengthPct = 100; fc.maxTorqueNm = 2.5f; fc.torqueLimitNm = 2.5f;
        EndstopConfig ec; ec.rangeRad = 1.0f; ec.stiffnessNm = 10.0f;
        CHECK(approx(finalTorque(255, 1.5f, fc, ec), -2.5f));  // 2.5 - 5.0 = -2.5 (clampado)
        CHECK(approx(finalTorque(0, 0.0f, fc, ec), 0.0f));
    }

    // 5) FfbController: HAL mockada, malha completa + segurança latched
    {
        FfbController ctrl;
        ctrl.force.totalStrengthPct = 100; ctrl.force.maxTorqueNm = 2.5f; ctrl.force.torqueLimitNm = 2.5f;
        ctrl.endstop.rangeRad = 1.0f; ctrl.endstop.stiffnessNm = 3.0f;
        ctrl.currentLimitA = 8.0f;
        MockEncoder enc; MockSense sense; MockMotor motor;

        // desabilitado → motor desligado, torque 0
        ctrl.enabled = false;
        CHECK(approx(ctrl.step(255, enc, sense, motor), 0.0f));
        CHECK(motor.disableCalls == 1 && motor.setCalls == 0);

        // habilitado, corrente ok, posição no centro → comanda o torque nominal
        ctrl.enabled = true; sense.a = sense.b = sense.c = 1.0f; enc.pos = 0.0f;
        CHECK(approx(ctrl.step(255, enc, sense, motor), 2.5f));
        CHECK(motor.setCalls == 1 && approx(motor.lastTorque, 2.5f));

        // sobrecorrente → desarma (latched) e desliga
        sense.a = 20.0f;
        CHECK(approx(ctrl.step(255, enc, sense, motor), 0.0f));
        CHECK(ctrl.tripped);
        // continua desarmado mesmo com corrente normal, até rearmar
        sense.a = 1.0f;
        CHECK(approx(ctrl.step(255, enc, sense, motor), 0.0f));
        ctrl.rearm();
        CHECK(approx(ctrl.step(255, enc, sense, motor), 2.5f));
    }

    std::printf("%s  — %d checks, %d fail(s)\n", g_fails ? "FALHOU" : "OK", g_checks, g_fails);
    return g_fails ? 1 : 0;
}
