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
#include "pi_controller.h"
#include "ffb_power.h"
#include "startup.h"
#include "force_reconstruct.h"
#include "cogging.h"
#include "filters.h"

#include <cstdio>
#include <cmath>

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
struct MockPower : IPowerSense {
    float bus = 24, mfet = 25, mot = 25;
    float busVoltage() override { return bus; }
    float mosfetTempC() override { return mfet; }
    float motorTempC() override { return mot; }
};
struct MockBrake : IBrakeResistor {
    float lastDuty = -1;
    void setDuty(float d) override { lastDuty = d; }
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

    // 6) M5 — curva de resposta + efeitos de condição do device (do encoder)
    {
        CHECK(approx(responseCurve(0.5f, 1.0f), 0.5f));    // linear = identidade
        CHECK(approx(responseCurve(0.5f, 2.0f), 0.25f));   // >1 suaviza o leve
        CHECK(approx(responseCurve(-0.5f, 2.0f), -0.25f)); // ímpar (preserva sinal)
        CHECK(approx(responseCurve(0.25f, 0.5f), 0.5f));   // <1 realça o leve

        CHECK(approx(springTorque(1.0f, 2.0f), -2.0f));    // mola puxa p/ o centro
        CHECK(approx(springTorque(-1.0f, 2.0f), 2.0f));
        CHECK(approx(damperTorque(3.0f, 0.5f), -1.5f));    // damper opõe a velocidade
        CHECK(approx(frictionTorque(0.01f, 0.4f), -0.4f)); // atrito opõe o movimento
        CHECK(approx(frictionTorque(-0.01f, 0.4f), 0.4f));
        CHECK(approx(frictionTorque(0.0f, 0.4f), 0.0f));   // parado = sem atrito

        CHECK(approx(slewLimit(2.5f, 0.0f, 0.5f), 0.5f));  // limita a variação
        CHECK(approx(slewLimit(2.5f, 0.0f, 0.0f), 2.5f));  // 0 = desligado
    }

    // 7) computeTorque (pipeline completo) + inversão + centragem sem força de jogo
    {
        ForceConfig fc; fc.totalStrengthPct = 100; fc.maxTorqueNm = 2.5f; fc.torqueLimitNm = 2.5f;
        EffectConfig ef; EndstopConfig ec; ec.rangeRad = 10.0f;  // sem soft-stop na faixa testada

        CHECK(approx(computeTorque(255, 0.0f, 0.0f, fc, ef, ec), 2.5f));   // só força do jogo
        fc.direction = -1.0f;                                              // inversão
        CHECK(approx(computeTorque(255, 0.0f, 0.0f, fc, ef, ec), -2.5f));
        fc.direction = 1.0f;

        ef.springNmPerRad = 1.0f;                                          // centragem do device
        CHECK(approx(computeTorque(0, 0.5f, 0.0f, fc, ef, ec), -0.5f));    // sem jogo, mola puxa p/ centro
    }

    // 8) FfbController com slew-rate: o torque sobe em degraus limitados (estado entre passos)
    {
        FfbController ctrl;
        ctrl.force.totalStrengthPct = 100; ctrl.force.maxTorqueNm = 2.5f; ctrl.force.torqueLimitNm = 2.5f;
        ctrl.endstop.rangeRad = 10.0f;
        ctrl.maxSlewNmPerStep = 0.5f;
        ctrl.enabled = true;
        MockEncoder enc; MockSense sense; MockMotor motor; sense.a = sense.b = sense.c = 1.0f;

        CHECK(approx(ctrl.step(255, enc, sense, motor), 0.5f));  // alvo 2.5, mas +0.5/passo
        CHECK(approx(ctrl.step(255, enc, sense, motor), 1.0f));
        CHECK(approx(ctrl.step(255, enc, sense, motor), 1.5f));
    }

    // 9) M2 — PI (malha fechada): termo P, acúmulo do I, anti-windup, reset
    {
        PiController pi; pi.kp = 2.0f; pi.ki = 0.0f; pi.outMin = -100; pi.outMax = 100;
        CHECK(approx(pi.update(10, 3, 1.0f), 14.0f));      // só P: 2*(10-3)

        PiController i; i.kp = 0.0f; i.ki = 1.0f; i.outMin = -5; i.outMax = 5;
        CHECK(approx(i.update(10, 0, 1.0f), 5.0f));        // I acumula 10 → clamp anti-windup a 5
        CHECK(approx(i.update(10, 0, 1.0f), 5.0f));        // segue saturado (windup contido)
        CHECK(approx(i.integral, 5.0f));                   // integrador clampado, não estoura
        i.reset();
        CHECK(approx(i.integral, 0.0f) && approx(i.update(0, 0, 1.0f), 0.0f));
    }

    // 10) M2 — brake resistor: histerese + duty proporcional
    {
        BrakeController b; b.cfg.onVoltage = 26; b.cfg.fullVoltage = 30; b.cfg.offVoltage = 25;
        CHECK(approx(b.update(24), 0.0f) && !b.isOn());    // abaixo → desligado
        CHECK(approx(b.update(28), 0.5f) && b.isOn());     // liga; (28-26)/(30-26)=0.5
        CHECK(approx(b.update(32), 1.0f));                 // acima de full → clamp 1
        CHECK(approx(b.update(25.5f), 0.0f) && b.isOn());  // histerese: >off, segue ligado, duty 0
        CHECK(approx(b.update(24), 0.0f) && !b.isOn());    // <off → desliga
    }

    // 11) M2 — PowerGuard: dump de regeneração + falha latched por sobretensão/sobretemperatura
    {
        PowerGuard g; g.overVoltageV = 30; g.overTempC = 80;
        g.brake.cfg.onVoltage = 26; g.brake.cfg.fullVoltage = 30; g.brake.cfg.offVoltage = 25;
        MockPower pw; MockBrake br;

        pw.bus = 28;                                       // regen eleva a tensão → dissipa
        CHECK(approx(g.step(pw, br), 0.5f));
        CHECK(approx(br.lastDuty, 0.5f) && !g.faulted);

        pw.bus = 33;                                       // sobretensão → FALHA
        g.step(pw, br);
        CHECK(g.faulted);

        PowerGuard g2; g2.overTempC = 80;                  // sobretemperatura também falha
        MockPower hot; hot.bus = 20; hot.mfet = 90; MockBrake br2;
        g2.step(hot, br2);
        CHECK(g2.faulted);
    }

    // 12) M1 — sequência de partida: inter-travamentos, Idle→Alinhamento→Rodando (+rampa), falha
    {
        StartupSequencer seq;
        seq.cfg.busMinV = 20; seq.cfg.busMaxV = 30; seq.cfg.tempMaxC = 80;
        seq.cfg.alignSeconds = 0.5f; seq.cfg.rampSeconds = 0.5f; seq.cfg.alignTorqueNm = 0.3f;

        // inter-travamentos
        StartupInputs ok{ true, false, 24.0f, 25.0f };
        CHECK(seq.interlocksOk(ok));
        CHECK(!seq.interlocksOk(StartupInputs{ true, false, 15.0f, 25.0f }));  // tensão baixa
        CHECK(!seq.interlocksOk(StartupInputs{ true, false, 33.0f, 25.0f }));  // tensão alta
        CHECK(!seq.interlocksOk(StartupInputs{ true, false, 24.0f, 90.0f }));  // quente
        CHECK(!seq.interlocksOk(StartupInputs{ true, true,  24.0f, 25.0f }));  // proteção em falha

        // Idle → Aligning (pediu força + interlocks ok)
        CHECK(seq.state == MotorState::Idle);
        seq.update(0.1f, ok);
        CHECK(seq.state == MotorState::Aligning && !seq.forceEnabled());
        CHECK(approx(seq.alignTorque(), 0.3f));                 // segura o rotor com torque baixo

        // ainda alinhando (tempo insuficiente) → depois Running
        seq.update(0.3f, ok); CHECK(seq.state == MotorState::Aligning);
        seq.update(0.3f, ok); CHECK(seq.state == MotorState::Running);   // 0.6 ≥ 0.5

        // rampa de subida 0→1
        CHECK(approx(seq.rampGain(), 0.0f));
        seq.update(0.25f, ok); CHECK(approx(seq.rampGain(), 0.5f) && seq.forceEnabled());
        seq.update(0.25f, ok); CHECK(approx(seq.rampGain(), 1.0f));
        seq.update(0.5f,  ok); CHECK(approx(seq.rampGain(), 1.0f));       // satura em 1

        // soltar o enable → volta a Idle
        seq.update(0.1f, StartupInputs{ false, false, 24.0f, 25.0f });
        CHECK(seq.state == MotorState::Idle && approx(seq.rampGain(), 0.0f));

        // falha tem prioridade em qualquer estado
        seq.update(0.1f, ok); seq.update(0.6f, ok);            // volta a Running
        CHECK(seq.state == MotorState::Running);
        seq.update(0.1f, StartupInputs{ true, true, 24.0f, 25.0f });   // proteção falhou
        CHECK(seq.state == MotorState::Fault && !seq.forceEnabled());

        // sai da falha só com clearFault(); re-entra se a causa persistir
        seq.clearFault(); CHECK(seq.state == MotorState::Idle);
        seq.update(0.1f, StartupInputs{ true, true, 24.0f, 25.0f });
        CHECK(seq.state == MotorState::Fault);                 // causa persiste → re-arma a falha
    }

    // 13) Reconstrução de força: rampa linear vs degrau (ZOH), retarget no meio, LPF, e a
    //     MÉTRICA de suavidade (o maior salto por tick cai muito vs. segurar o valor)
    {
        // rampa: um degrau 0→10 vira 2.5/5/7.5/10 (janela de 4 ticks)
        ForceReconstructor r; r.cfg.steps = 4; r.cfg.lpfAlpha = 0.0f;
        r.setTarget(10.0f);
        CHECK(approx(r.tick(), 2.5f));
        CHECK(approx(r.tick(), 5.0f));
        CHECK(approx(r.tick(), 7.5f));
        CHECK(approx(r.tick(), 10.0f));
        CHECK(approx(r.tick(), 10.0f));   // segura no alvo entre atualizações

        // retarget no meio da janela: recomeça a interpolação do ponto atual
        ForceReconstructor r2; r2.cfg.steps = 4;
        r2.setTarget(10.0f); r2.tick(); r2.tick();     // current = 5
        r2.setTarget(0.0f);                            // novo alvo a partir de 5
        CHECK(approx(r2.tick(), 3.75f));
        CHECK(approx(r2.tick(), 2.5f));

        // ZOH (steps ≤ 1): assume o alvo imediatamente (degrau)
        ForceReconstructor z; z.cfg.steps = 1;
        z.setTarget(10.0f);
        CHECK(approx(z.tick(), 10.0f));

        // métrica de suavidade: maior salto por tick << degrau cru (medido a partir do valor
        // ANTES do setTarget, pois no ZOH o salto acontece já no setTarget)
        auto maxJumpAfterStep = [](ForceReconstructor& rec, float target, int n) {
            float prev = rec.current(), m = 0.0f;
            rec.setTarget(target);
            for (int i = 0; i < n; ++i) { float v = rec.tick(); float d = v - prev; if (d < 0) d = -d; if (d > m) m = d; prev = v; }
            return m;
        };
        ForceReconstructor smooth; smooth.cfg.steps = 8;
        ForceReconstructor zoh;    zoh.cfg.steps = 1;
        const float jS = maxJumpAfterStep(smooth, 100.0f, 8);
        const float jZ = maxJumpAfterStep(zoh, 100.0f, 8);
        CHECK(approx(jS, 12.5f));      // 100/8 por tick
        CHECK(jZ >= 99.0f);            // ZOH salta ~100 de uma vez
        CHECK(jS < jZ);               // reconstrução suaviza (menos salto = menos "notch")

        // LPF opcional: mesmo com degrau, a saída se aproxima do alvo gradualmente
        ForceReconstructor f; f.cfg.steps = 1; f.cfg.lpfAlpha = 0.5f;
        f.setTarget(10.0f);
        const float a = f.tick(), b = f.tick(), c = f.tick();
        CHECK(a > 0.0f && a < b && b < c && c < 10.0f);   // sobe monotonicamente rumo ao alvo
    }

    // 14) Cogging: interpolação/wrap do mapa + calibração reconstrói e CANCELA um ripple sintético
    {
        const float twoPi = 6.28318530718f;

        // interpolação + wrap-around num mapa pequeno conhecido
        CoggingMap<4> m; m.table[0] = 0; m.table[1] = 1; m.table[2] = 2; m.table[3] = 3;
        CHECK(approx(m.compensation(0.0f), 0.0f));
        CHECK(approx(m.compensation(twoPi * 1.0f / 4.0f), 1.0f));   // bin 1
        CHECK(approx(m.compensation(twoPi * 0.125f), 0.5f));        // meio entre bin0 e bin1
        CHECK(approx(m.compensation(twoPi * 3.5f / 4.0f), 1.5f));   // wrap: entre bin3(3) e bin0(0)

        // calibração: gira devagar amostrando torque = 0.2·sin(pos) + 0.5 (DC de carga/atrito).
        // A compensação deve reconstruir −0.2·sin(pos) (DC removido, sinal invertido).
        CoggingCalibrator<64> cal;
        for (int k = 0; k < 2000; ++k) {
            const float pos = twoPi * static_cast<float>(k) / 2000.0f;
            cal.addSample(pos, 0.2f * std::sin(pos) + 0.5f);
        }
        CoggingMap<64> cog; cal.finish(cog);

        CHECK(approx(cog.compensation(0.0f),          0.0f, 0.02f));  // −0.2·sin(0)
        CHECK(approx(cog.compensation(twoPi * 0.25f), -0.2f, 0.02f)); // −0.2·sin(π/2)
        CHECK(approx(cog.compensation(twoPi * 0.75f),  0.2f, 0.02f)); // −0.2·sin(3π/2)

        // prova do cancelamento: torque_bruto (com cogging) + compensação ≈ liso (só o DC)
        float worst = 0.0f;
        for (int k = 0; k < 100; ++k) {
            const float pos = twoPi * static_cast<float>(k) / 100.0f;
            const float raw = 0.2f * std::sin(pos) + 0.5f;   // o que o motor faria (com ripple)
            const float compensated = raw + cog.compensation(pos);
            const float err = std::fabs(compensated - 0.5f); // deveria sobrar só o DC
            if (err > worst) worst = err;
        }
        CHECK(worst < 0.02f);   // ripple de ±0.2 reduzido a <0.02 → ~10× mais liso
    }

    // 15) Filtros DSP: low-pass (passa DC, mata Nyquist), notch (passa DC, rejeita f0), velocidade
    {
        const float twoPi = 6.28318530718f;
        const float fs = 1000.0f;

        // low-pass: ganho DC = 1 (constante passa), e o sinal em Nyquist (±1 alternado) é esmagado
        Biquad lp = makeLowPass(50.0f, fs, 0.707f);
        for (int i = 0; i < 400; ++i) lp.process(1.0f);
        CHECK(approx(lp.process(1.0f), 1.0f, 0.01f));          // DC preservado
        lp.reset();
        float lastN = 0.0f;
        for (int i = 0; i < 400; ++i) lastN = lp.process((i & 1) ? -1.0f : 1.0f);
        CHECK(std::fabs(lastN) < 0.1f);                        // Nyquist atenuado

        // notch @ 50 Hz: DC passa; uma senoide EM 50 Hz é fortemente rejeitada
        Biquad nt = makeNotch(50.0f, fs, 5.0f);
        for (int i = 0; i < 400; ++i) nt.process(1.0f);
        CHECK(approx(nt.process(1.0f), 1.0f, 0.02f));          // DC preservado (não é a banda rejeitada)
        nt.reset();
        float inRms = 0.0f, outRms = 0.0f; int cnt = 0;
        for (int i = 0; i < 600; ++i) {
            const float x = std::sin(twoPi * 50.0f * static_cast<float>(i) / fs);
            const float y = nt.process(x);
            if (i >= 400) { inRms += x * x; outRms += y * y; ++cnt; }
        }
        inRms = std::sqrt(inRms / cnt); outRms = std::sqrt(outRms / cnt);
        CHECK(outRms < 0.2f * inRms);                          // ressonância suprimida (>5× de atenuação)

        // estimador de velocidade: rampa de posição (v = 2 rad/s) → estimativa converge p/ 2
        VelocityEstimator ve; ve.lpf = makeLowPass(30.0f, fs, 0.707f);
        const float v = 2.0f, dt = 1.0f / fs;
        float est = 0.0f;
        for (int i = 1; i <= 600; ++i) est = ve.update(v * dt * static_cast<float>(i), dt);
        CHECK(approx(est, 2.0f, 0.02f));                       // segue a velocidade real, suave
    }

    std::printf("%s  — %d checks, %d fail(s)\n", g_fails ? "FALHOU" : "OK", g_checks, g_fails);
    return g_fails ? 1 : 0;
}
