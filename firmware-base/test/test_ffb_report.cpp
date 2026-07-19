// ============================================================================
//  DriveLab Firmware
//  test_ffb_report.cpp — Testes de HOST do parser puro de OUT report PID
//  (decodifica bytes HID Set Constant Force / Effect Operation / etc, sem placa).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Roda sem placa nenhuma: firmware-base/test/run.sh

#include "../src/m05/ffb_report.h"
#include "../include/ffb_hid_descriptor.h"

#include <cstdio>

// ----- micro-harness (mesmo padrão de test_ffb_brain.cpp) -----
static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

int main() {
    // Set Constant Force: [RID_PID_SET_CONSTANT_FORCE, effectBlock=1, magLo, magHi] = +1000
    {
        uint8_t cf[] = { RID_PID_SET_CONSTANT_FORCE, 0x01, 0xE8, 0x03 };
        FfbOut o = ffb_parse_out(cf, sizeof(cf));
        CHECK(o.type == FFB_SET_CONSTANT_FORCE);
        CHECK(o.effectBlock == 1);
        CHECK(o.constantForce == 1000);
    }

    // Set Constant Force negativo
    {
        uint8_t cf[] = { RID_PID_SET_CONSTANT_FORCE, 0x02, 0x18, 0xFC }; // 0xFC18 = -1000
        FfbOut o = ffb_parse_out(cf, sizeof(cf));
        CHECK(o.type == FFB_SET_CONSTANT_FORCE);
        CHECK(o.effectBlock == 2);
        CHECK(o.constantForce == -1000);
    }

    // Effect Operation start: [RID_PID_EFFECT_OPERATION, block=1, op=1(start), loop]
    {
        uint8_t eo[] = { RID_PID_EFFECT_OPERATION, 0x01, 0x01, 0x01 };
        FfbOut o = ffb_parse_out(eo, sizeof(eo));
        CHECK(o.type == FFB_EFFECT_OPERATION);
        CHECK(o.effectBlock == 1);
        CHECK(o.op == 1);
    }

    // Block Load / Device Control / Set Effect: apenas classificação de tipo
    {
        uint8_t bl[] = { RID_PID_BLOCK_LOAD, 0x01 };
        CHECK(ffb_parse_out(bl, sizeof(bl)).type == FFB_BLOCK_LOAD);

        uint8_t dc[] = { RID_PID_DEVICE_CONTROL, 0x04 };
        CHECK(ffb_parse_out(dc, sizeof(dc)).type == FFB_DEVICE_CONTROL);

        uint8_t se[] = { RID_PID_SET_EFFECT, 0x01, 0x01 };
        CHECK(ffb_parse_out(se, sizeof(se)).type == FFB_SET_EFFECT);
    }

    // desconhecido não trava
    {
        uint8_t un[] = { 0xEE, 0x00 };
        CHECK(ffb_parse_out(un, sizeof(un)).type == FFB_UNKNOWN);
    }

    // len curto/zero não trava e retorna FFB_UNKNOWN (guarda antes de indexar)
    {
        CHECK(ffb_parse_out(nullptr, 0).type == FFB_UNKNOWN);
        uint8_t shortCf[] = { RID_PID_SET_CONSTANT_FORCE, 0x01 }; // faltam bytes de magnitude
        CHECK(ffb_parse_out(shortCf, sizeof(shortCf)).type == FFB_UNKNOWN);
        uint8_t shortEo[] = { RID_PID_EFFECT_OPERATION, 0x01 }; // falta o byte de op
        CHECK(ffb_parse_out(shortEo, sizeof(shortEo)).type == FFB_UNKNOWN);
    }

    std::printf("%s  — %d checks, %d fail(s)\n", g_fails ? "FALHOU" : "OK", g_checks, g_fails);
    return g_fails ? 1 : 0;
}
