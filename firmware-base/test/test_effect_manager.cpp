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

    std::printf("%s  — %d checks, %d fail(s)\n", g_fails ? "FALHOU" : "OK", g_checks, g_fails);
    return g_fails ? 1 : 0;
}
