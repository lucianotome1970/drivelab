// ============================================================================
//  DriveLab Firmware
//  test_base_cfg.cpp — Testes de HOST do modelo BaseCfg (settings da base):
//  seed de defaults, leitura/escrita LE por tipo, guards de id desconhecido
//  e len curto. Roda sem placa (mesmo padrão de test_ffb_report.cpp).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Roda sem placa nenhuma: firmware-base/test/run.sh

#include "../lib/base_shared/base_cfg.h"

#include <cstdio>
#include <cstring>

// ----- micro-harness (mesmo padrão de test_ffb_report.cpp) -----
static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

int main() {
    // ----- baseSeedDefaults: valores do schema -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        CHECK(c.motionRange == 900);
        CHECK(c.totalStrength == 100);
        CHECK(c.forceDirection == 1);
        CHECK(c.encoderCpr == 4000);
        CHECK(c.currentP > 0.0499f && c.currentP < 0.0501f);
        CHECK(c.currentI > 9.999f && c.currentI < 10.001f);
        CHECK(c.encoderType == 0);
        CHECK(c.reconstructionSteps == 0);
        CHECK(c.reconstructionLpf == 0);
        CHECK(c.outputFilterHz == 0);
        CHECK(c.oscGuardEnable == 0);
        CHECK(c.endstopDamping == 0);
        CHECK(c.linearity == 100);
        CHECK(c.coggingEnable == 0);
        CHECK(c.slewRate == 0);
    }

    // ----- baseTypeForField -----
    {
        CHECK(baseTypeForField(BID_ENCODER_CPR) == BT_UINT16);
        CHECK(baseTypeForField(BID_FORCE_DIRECTION) == BT_INT8);
        CHECK(baseTypeForField(BID_CURRENT_P) == BT_FLOAT);
        CHECK(baseTypeForField(BID_TOTAL_STRENGTH) == BT_UINT8);
        CHECK(baseTypeForField(200) == 0xFF);  // id desconhecido
    }

    // ----- round-trip UInt16 (EncoderCpr) -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        uint8_t type, buf[8];

        int n = baseReadField(c, BID_ENCODER_CPR, &type, buf);
        CHECK(n == 2);
        CHECK(type == BT_UINT16);
        CHECK((buf[0] | (buf[1] << 8)) == 4000);

        uint16_t newVal = 54321;
        uint8_t w[2] = { (uint8_t)(newVal & 0xFF), (uint8_t)((newVal >> 8) & 0xFF) };
        baseWriteField(c, BID_ENCODER_CPR, BT_UINT16, w, sizeof(w));
        CHECK(c.encoderCpr == 54321);

        n = baseReadField(c, BID_ENCODER_CPR, &type, buf);
        CHECK(n == 2);
        CHECK((buf[0] | (buf[1] << 8)) == 54321);
    }

    // ----- round-trip Int8 (ForceDirection) -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        uint8_t type, buf[8];

        int n = baseReadField(c, BID_FORCE_DIRECTION, &type, buf);
        CHECK(n == 1);
        CHECK(type == BT_INT8);
        CHECK((int8_t)buf[0] == 1);

        int8_t newVal = -1;
        uint8_t w[1] = { (uint8_t)newVal };
        baseWriteField(c, BID_FORCE_DIRECTION, BT_INT8, w, sizeof(w));
        CHECK(c.forceDirection == -1);

        n = baseReadField(c, BID_FORCE_DIRECTION, &type, buf);
        CHECK((int8_t)buf[0] == -1);
    }

    // ----- round-trip Float (CurrentP) -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        uint8_t type, buf[8];

        int n = baseReadField(c, BID_CURRENT_P, &type, buf);
        CHECK(n == 4);
        CHECK(type == BT_FLOAT);
        float got;
        std::memcpy(&got, buf, 4);
        CHECK(got > 0.0499f && got < 0.0501f);

        float newVal = 3.75f;
        uint8_t w[4];
        std::memcpy(w, &newVal, 4);
        baseWriteField(c, BID_CURRENT_P, BT_FLOAT, w, sizeof(w));
        CHECK(c.currentP > 3.7499f && c.currentP < 3.7501f);

        n = baseReadField(c, BID_CURRENT_P, &type, buf);
        std::memcpy(&got, buf, 4);
        CHECK(got > 3.7499f && got < 3.7501f);
    }

    // ----- round-trip UInt8 (Linearity) -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        uint8_t type, buf[8];

        int n = baseReadField(c, BID_LINEARITY, &type, buf);
        CHECK(n == 1);
        CHECK(type == BT_UINT8);
        CHECK(buf[0] == 100);

        uint8_t newVal = 150;
        uint8_t w[1] = { newVal };
        baseWriteField(c, BID_LINEARITY, BT_UINT8, w, sizeof(w));
        CHECK(c.linearity == 150);

        n = baseReadField(c, BID_LINEARITY, &type, buf);
        CHECK(n == 1);
        CHECK(buf[0] == 150);
    }

    // ----- round-trip UInt16 LE (OutputFilterHz) -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        uint8_t type, buf[8];

        int n = baseReadField(c, BID_OUTPUT_FILTER_HZ, &type, buf);
        CHECK(n == 2);
        CHECK(type == BT_UINT16);
        CHECK((buf[0] | (buf[1] << 8)) == 0);

        uint16_t newVal = 1234;
        uint8_t w[2] = { (uint8_t)(newVal & 0xFF), (uint8_t)((newVal >> 8) & 0xFF) };
        baseWriteField(c, BID_OUTPUT_FILTER_HZ, BT_UINT16, w, sizeof(w));
        CHECK(c.outputFilterHz == 1234);

        n = baseReadField(c, BID_OUTPUT_FILTER_HZ, &type, buf);
        CHECK(n == 2);
        CHECK((buf[0] | (buf[1] << 8)) == 1234);
    }

    // ----- round-trip UInt8 (SlewRate) -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        uint8_t type, buf[8];

        int n = baseReadField(c, BID_SLEW_RATE, &type, buf);
        CHECK(n == 1);
        CHECK(type == BT_UINT8);
        CHECK(buf[0] == 0);

        uint8_t newVal = 42;
        uint8_t w[1] = { newVal };
        baseWriteField(c, BID_SLEW_RATE, BT_UINT8, w, sizeof(w));
        CHECK(c.slewRate == 42);

        n = baseReadField(c, BID_SLEW_RATE, &type, buf);
        CHECK(n == 1);
        CHECK(buf[0] == 42);
    }

    // ----- guard: id desconhecido -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        uint8_t type = 0xAA, buf[8];
        std::memset(buf, 0xAA, sizeof(buf));
        int n = baseReadField(c, 200, &type, buf);
        CHECK(n == 0);
        // não deve escrever nada
        CHECK(type == 0xAA);
        CHECK(buf[0] == 0xAA);

        // write com id desconhecido não deve alterar nada nem crashar
        uint8_t w[4] = { 1, 2, 3, 4 };
        BaseCfg before = c;
        baseWriteField(c, 200, BT_UINT8, w, sizeof(w));
        CHECK(std::memcmp(&c, &before, sizeof(BaseCfg)) == 0);
    }

    // ----- guard: len curto -----
    {
        BaseCfg c;
        baseSeedDefaults(c);
        uint16_t before = c.encoderCpr;

        uint8_t w1[1] = { 0xFF };  // UInt16 precisa de 2 bytes, só manda 1
        baseWriteField(c, BID_ENCODER_CPR, BT_UINT16, w1, sizeof(w1));
        CHECK(c.encoderCpr == before);  // não deve mudar

        float beforeF = c.currentP;
        uint8_t w3[3] = { 1, 2, 3 };  // Float precisa de 4 bytes, só manda 3
        baseWriteField(c, BID_CURRENT_P, BT_FLOAT, w3, sizeof(w3));
        CHECK(c.currentP == beforeF);  // não deve mudar

        // len == 0 não deve crashar
        baseWriteField(c, BID_ENCODER_CPR, BT_UINT16, nullptr, 0);
        CHECK(c.encoderCpr == before);
    }

    std::printf("base_cfg: %d checks, %d fails\n", g_checks, g_fails);
    return g_fails ? 1 : 0;
}
