// ============================================================================
//  DriveLab Firmware
//  test_effect_manager.cpp — Testes de HOST do banco de slots de efeitos
//  (EffectManager): roteamento de OUT reports PID por effectBlockIndex,
//  operation (start/startSolo/stop), freeBlock, reset, stopAll — sem placa.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Roda sem placa nenhuma: firmware-base/test/run.sh

#include "../lib/brain/effect_manager.h"

#include <cmath>
#include <cstdio>

// ----- micro-harness (mesmo padrão de test_ffb_effects.cpp) -----
static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

int main() {
    // ---- SetEffect + SetPeriodic no mesmo slot (block=1) — params preservados ----
    {
        EffectManager mgr;

        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01;
        setEffect[1] = 1;      // block
        setEffect[2] = 4;      // type = Sine
        mgr.handleReport(setEffect, sizeof(setEffect), 1000);

        CHECK(mgr.slot(0).type == FxType::Sine);
        CHECK(mgr.slot(0).block == 1);

        uint8_t setPeriodic[12] = {0};
        setPeriodic[0] = 0x04;
        setPeriodic[1] = 1;    // block
        setPeriodic[2] = 0x88; setPeriodic[3] = 0x13; // magnitude = 5000
        mgr.handleReport(setPeriodic, sizeof(setPeriodic), 1000);

        CHECK(mgr.slot(0).magnitude16 == 5000);
        CHECK(mgr.slot(0).type == FxType::Sine); // preservado

        // ---- EffectOperation start (state=1) ----
        uint8_t opStart[3] = {0x0A, 1, 1}; // reportId, block, state=start
        mgr.handleReport(opStart, sizeof(opStart), 5000);
        CHECK(mgr.slot(0).active == true);
        CHECK(mgr.slot(0).startMs == 5000);

        // ---- EffectOperation stop (state=3) ----
        uint8_t opStop[3] = {0x0A, 1, 3};
        mgr.handleReport(opStop, sizeof(opStop), 6000);
        CHECK(mgr.slot(0).active == false);
    }

    // ---- operation() direto: start/stop ----
    {
        EffectManager mgr;
        mgr.operation(1, 1, 123); // start block 1
        CHECK(mgr.slot(0).active == true);
        CHECK(mgr.slot(0).startMs == 123);

        mgr.operation(1, 3, 200); // stop block 1
        CHECK(mgr.slot(0).active == false);
    }

    // ---- startSolo (state=2): ativa slots 1 e 2, solo no 2 -> só block 2 ativo ----
    {
        EffectManager mgr;
        mgr.operation(1, 1, 10); // start block 1 (slot 0)
        mgr.operation(2, 1, 10); // start block 2 (slot 1)
        mgr.operation(3, 1, 10); // start block 3 (slot 2)
        CHECK(mgr.slot(0).active == true);
        CHECK(mgr.slot(1).active == true);
        CHECK(mgr.slot(2).active == true);

        mgr.operation(2, 2, 20); // startSolo block 2

        CHECK(mgr.slot(0).active == false);
        CHECK(mgr.slot(1).active == true);
        CHECK(mgr.slot(1).startMs == 20);
        CHECK(mgr.slot(2).active == false);
    }

    // ---- freeBlock ----
    {
        EffectManager mgr;
        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01; setEffect[1] = 1; setEffect[2] = 4; // Sine block 1
        mgr.handleReport(setEffect, sizeof(setEffect), 0);
        mgr.operation(1, 1, 0);
        CHECK(mgr.slot(0).type == FxType::Sine);
        CHECK(mgr.slot(0).active == true);

        mgr.freeBlock(1);
        CHECK(mgr.slot(0).type == FxType::None);
        CHECK(mgr.slot(0).active == false);
    }

    // ---- DeviceControl reset (buf={0x0C,0x08}) — todos inativos + limpos ----
    {
        EffectManager mgr;
        mgr.operation(1, 1, 0);
        mgr.operation(2, 1, 0);
        CHECK(mgr.slot(0).active == true);
        CHECK(mgr.slot(1).active == true);

        uint8_t devCtrl[2] = {0x0C, 0x08}; // reset bit
        mgr.handleReport(devCtrl, sizeof(devCtrl), 999);

        CHECK(mgr.slot(0).active == false);
        CHECK(mgr.slot(1).active == false);
        CHECK(mgr.slot(0).type == FxType::None);
    }

    // ---- DeviceControl stopAll (buf={0x0C,0x04}) — inativos mas params ficam ----
    {
        EffectManager mgr;
        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01; setEffect[1] = 1; setEffect[2] = 4; // Sine block 1
        mgr.handleReport(setEffect, sizeof(setEffect), 0);
        mgr.operation(1, 1, 0);
        CHECK(mgr.slot(0).active == true);

        uint8_t devCtrl[2] = {0x0C, 0x04}; // stopAll bit
        mgr.handleReport(devCtrl, sizeof(devCtrl), 999);

        CHECK(mgr.slot(0).active == false);
        CHECK(mgr.slot(0).type == FxType::Sine); // params preservados
    }

    // ---- block fora de faixa (buf[1]=200) — ignora sem crash ----
    {
        EffectManager mgr;
        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01; setEffect[1] = 200; setEffect[2] = 4;
        mgr.handleReport(setEffect, sizeof(setEffect), 0); // não deve crashar

        mgr.operation(200, 1, 0);  // idem
        mgr.freeBlock(200);        // idem

        for (int i = 0; i < kEffectSlots; ++i) {
            CHECK(mgr.slot(i).type == FxType::None);
            CHECK(mgr.slot(i).active == false);
        }
    }

    // ---- block == 0 (0-1 = -1 fora de faixa) — ignora sem crash ----
    {
        EffectManager mgr;
        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01; setEffect[1] = 0; setEffect[2] = 4;
        mgr.handleReport(setEffect, sizeof(setEffect), 0);
        for (int i = 0; i < kEffectSlots; ++i) {
            CHECK(mgr.slot(i).type == FxType::None);
        }
    }

    // ---- len < 2 -> ignora ----
    {
        EffectManager mgr;
        uint8_t buf[1] = {0x01};
        mgr.handleReport(buf, sizeof(buf), 0); // não deve crashar / não deve alterar nada
        for (int i = 0; i < kEffectSlots; ++i) {
            CHECK(mgr.slot(i).type == FxType::None);
        }

        mgr.handleReport(nullptr, 0, 0); // nullptr também não deve crashar
    }

    // ---- reset() limpa tudo ----
    {
        EffectManager mgr;
        mgr.operation(1, 1, 0);
        mgr.operation(2, 1, 0);
        mgr.reset();
        for (int i = 0; i < kEffectSlots; ++i) {
            CHECK(mgr.slot(i).type == FxType::None);
            CHECK(mgr.slot(i).active == false);
        }
    }

    // ---- reportId desconhecido -> ignora ----
    {
        EffectManager mgr;
        uint8_t buf[3] = {0xFF, 1, 1};
        mgr.handleReport(buf, sizeof(buf), 0);
        CHECK(mgr.slot(0).type == FxType::None);
    }

    // ==== computeForce ====

    // ---- manager vazio -> 0 ----
    {
        EffectManager mgr;
        CHECK(std::fabs(mgr.computeForce(0.0f, 0.0f, 0)) < 1e-3f);
    }

    // ---- slot inativo -> 0 ----
    {
        EffectManager mgr;
        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01; setEffect[1] = 1; setEffect[2] = 1; // Constant, block 1
        mgr.handleReport(setEffect, sizeof(setEffect), 0);
        uint8_t setConst[6] = {0};
        setConst[0] = 0x05; setConst[1] = 1;
        setConst[2] = 0x00; setConst[3] = 0x40; // magnitude = 16384
        mgr.handleReport(setConst, sizeof(setConst), 0);
        // não ativado
        CHECK(std::fabs(mgr.computeForce(0.0f, 0.0f, 0)) < 1e-3f);
    }

    // ---- Constant mag=16384, gain=255 -> computeForce() PULA Constant (0
    // de propósito) -- integração Task 4 (firmware-base/lib/brain/
    // ffb_engine.h): a força constante já flui pelo ForceReconstructor
    // (SP1, hostF), então o EffectManager a ignora aqui para não somar em
    // dobro no engine. ----
    {
        EffectManager mgr;
        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01; setEffect[1] = 1; setEffect[2] = 1; // Constant, block 1
        setEffect[11] = 255; // gain
        mgr.handleReport(setEffect, sizeof(setEffect), 0);
        uint8_t setConst[6] = {0};
        setConst[0] = 0x05; setConst[1] = 1;
        setConst[2] = 0x00; setConst[3] = 0x40; // magnitude = 16384 (LE)
        mgr.handleReport(setConst, sizeof(setConst), 0);
        mgr.operation(1, 1, 0); // start

        float f = mgr.computeForce(0.0f, 0.0f, 0);
        CHECK(std::fabs(f) < 1e-3f);
    }

    // ---- Sine mag16=32767, period=1000, t=250 (x=0.25) -> pico ~= +255 ----
    {
        EffectManager mgr;
        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01; setEffect[1] = 1; setEffect[2] = 4; // Sine, block 1
        setEffect[11] = 255; // gain
        mgr.handleReport(setEffect, sizeof(setEffect), 0);

        uint8_t setPeriodic[12] = {0};
        setPeriodic[0] = 0x04; setPeriodic[1] = 1;
        setPeriodic[2] = 0xFF; setPeriodic[3] = 0x7F; // magnitude16 = 32767
        setPeriodic[4] = 0x00; setPeriodic[5] = 0x00; // offset = 0
        setPeriodic[6] = 0x00; setPeriodic[7] = 0x00; // phase = 0
        setPeriodic[8] = 0xE8; setPeriodic[9] = 0x03; setPeriodic[10] = 0x00; setPeriodic[11] = 0x00; // period = 1000
        mgr.handleReport(setPeriodic, sizeof(setPeriodic), 0);
        mgr.operation(1, 1, 0); // start at t=0

        float fPeak = mgr.computeForce(0.0f, 0.0f, 250); // x = 0.25 -> sin(pi/2) = 1
        CHECK(std::fabs(fPeak - 255.0f) < 1.0f);

        float fZero = mgr.computeForce(0.0f, 0.0f, 500); // x = 0.5 -> sin(pi) = 0
        CHECK(std::fabs(fZero) < 1.0f);
    }

    // ---- Spring posCoeff=32767, pos > center -> força negativa ----
    {
        EffectManager mgr;
        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01; setEffect[1] = 1; setEffect[2] = 8; // Spring, block 1
        setEffect[11] = 255; // gain
        mgr.handleReport(setEffect, sizeof(setEffect), 0);

        uint8_t setCond[15] = {0};
        setCond[0] = 0x03; setCond[1] = 1;
        setCond[3] = 0x00; setCond[4] = 0x00; // centerOffset = 0
        setCond[5] = 0xFF; setCond[6] = 0x7F; // posCoeff = 32767
        setCond[7] = 0x00; setCond[8] = 0x00; // negCoeff = 0
        setCond[9] = 0xFF; setCond[10] = 0x7F;  // posSat = 32767
        setCond[11] = 0xFF; setCond[12] = 0x7F; // negSat = 32767
        setCond[13] = 0x00; setCond[14] = 0x00; // deadBand = 0
        mgr.handleReport(setCond, sizeof(setCond), 0);
        mgr.operation(1, 1, 0);

        // posRad = kMaxPosRad/2 -> metric = 0.5
        float f = mgr.computeForce(EffectManager::kMaxPosRad / 2.0f, 0.0f, 0);
        CHECK(f < 0.0f);
        CHECK(std::fabs(f - (-127.5f)) < 2.0f);
    }

    // ---- Damper vel>0 -> força negativa ----
    {
        EffectManager mgr;
        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01; setEffect[1] = 1; setEffect[2] = 9; // Damper, block 1
        setEffect[11] = 255; // gain
        mgr.handleReport(setEffect, sizeof(setEffect), 0);

        uint8_t setCond[15] = {0};
        setCond[0] = 0x03; setCond[1] = 1;
        setCond[5] = 0xFF; setCond[6] = 0x7F; // posCoeff = 32767
        setCond[9] = 0xFF; setCond[10] = 0x7F;  // posSat = 32767
        setCond[11] = 0xFF; setCond[12] = 0x7F; // negSat = 32767
        mgr.handleReport(setCond, sizeof(setCond), 0);
        mgr.operation(1, 1, 0);

        float f = mgr.computeForce(0.0f, EffectManager::kMaxVel / 2.0f, 0);
        CHECK(f < 0.0f);
    }

    // ---- dois efeitos ativos -> soma (usa Ramp, não Constant -- Constant é
    // pulado por computeForce() desde a integração Task 4, ver teste acima).
    // Ramp com duration=0 -> p=1.0 sempre -> força = rampEnd (constante no
    // tempo), útil aqui só para exercitar a soma de dois slots ativos. ----
    {
        EffectManager mgr;
        uint8_t setEffect1[16] = {0};
        setEffect1[0] = 0x01; setEffect1[1] = 1; setEffect1[2] = 2; // Ramp, block 1
        setEffect1[11] = 255;
        mgr.handleReport(setEffect1, sizeof(setEffect1), 0);
        uint8_t setRamp1[6] = {0};
        setRamp1[0] = 0x06; setRamp1[1] = 1;
        setRamp1[2] = 0x00; setRamp1[3] = 0x20; // rampStart = 8192 (LE)
        setRamp1[4] = 0x00; setRamp1[5] = 0x20; // rampEnd   = 8192 (LE)
        mgr.handleReport(setRamp1, sizeof(setRamp1), 0);
        mgr.operation(1, 1, 0);

        uint8_t setEffect2[16] = {0};
        setEffect2[0] = 0x01; setEffect2[1] = 2; setEffect2[2] = 2; // Ramp, block 2
        setEffect2[11] = 255;
        mgr.handleReport(setEffect2, sizeof(setEffect2), 0);
        uint8_t setRamp2[6] = {0};
        setRamp2[0] = 0x06; setRamp2[1] = 2;
        setRamp2[2] = 0x00; setRamp2[3] = 0x20; // rampStart = 8192 (LE)
        setRamp2[4] = 0x00; setRamp2[5] = 0x20; // rampEnd   = 8192 (LE)
        mgr.handleReport(setRamp2, sizeof(setRamp2), 0);
        mgr.operation(2, 1, 0);

        float f = mgr.computeForce(0.0f, 0.0f, 0);
        // cada um ~= 63.7 -> soma ~= 127.4
        CHECK(std::fabs(f - 127.0f) < 1.0f);
    }

    // ---- efeito expirado (duration passou) -> não contribui ----
    {
        EffectManager mgr;
        uint8_t setEffect[16] = {0};
        setEffect[0] = 0x01; setEffect[1] = 1; setEffect[2] = 1; // Constant, block 1
        setEffect[3] = 0x64; setEffect[4] = 0x00; // duration = 100
        setEffect[11] = 255;
        mgr.handleReport(setEffect, sizeof(setEffect), 0);
        uint8_t setConst[6] = {0};
        setConst[0] = 0x05; setConst[1] = 1;
        setConst[2] = 0x00; setConst[3] = 0x40; // magnitude = 16384
        mgr.handleReport(setConst, sizeof(setConst), 0);
        mgr.operation(1, 1, 0); // startMs = 0

        CHECK(std::fabs(mgr.computeForce(0.0f, 0.0f, 200)) < 1e-3f); // 200 >= 100 -> expirado
    }

    std::printf("%s  — %d checks, %d fail(s)\n", g_fails ? "FALHOU" : "OK", g_checks, g_fails);
    return g_fails ? 1 : 0;
}
