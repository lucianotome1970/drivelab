// ============================================================================
//  DriveLab Firmware
//  test_ffb_effects.cpp — Testes de HOST do parser puro de OUT reports PID
//  de efeitos (Set Effect / Envelope / Condition / Periodic / Constant /
//  Ramp), decodificando bytes conhecidos em FxEffect, sem placa.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Roda sem placa nenhuma: firmware-base/test/run.sh

#include "../lib/base_shared/ffb_effects.h"

#include <cstdio>

// ----- micro-harness (mesmo padrão de test_ffb_report.cpp) -----
static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

int main() {
    // ---- Set Effect (0x01): type=Spring(8), block=3, duration, startDelay,
    // gain, enableAxis, directionX ----
    {
        uint8_t buf[16] = {0};
        buf[0] = 0x01;              // ReportID
        buf[1] = 3;                 // effectBlockIndex
        buf[2] = 8;                 // effectType = Spring
        buf[3] = 0xF4; buf[4] = 0x01; // duration = 500
        buf[9] = 0x64; buf[10] = 0x00; // startDelay = 100
        buf[11] = 200;               // gain
        buf[13] = 0x01;              // enableAxis
        buf[14] = 0x10; buf[15] = 0x27; // directionX = 0x2710 = 10000 (100.00 deg)

        FxEffect e;
        bool ok = fxDecodeSetEffect(buf, sizeof(buf), e);
        CHECK(ok);
        CHECK(e.block == 3);
        CHECK(e.type == FxType::Spring);
        CHECK(e.durationMs == 500);
        CHECK(e.startMs == 100);
        CHECK(e.gain == 200);
        CHECK(e.directionCentideg == 10000);
    }

    // duration == 0 => infinito (armazena 0)
    {
        uint8_t buf[16] = {0};
        buf[0] = 0x01; buf[1] = 1; buf[2] = 4; // Sine
        buf[3] = 0; buf[4] = 0; // duration = 0
        FxEffect e;
        CHECK(fxDecodeSetEffect(buf, sizeof(buf), e));
        CHECK(e.durationMs == 0);
        CHECK(e.type == FxType::Sine);
    }

    // Set Effect: buffer curto demais -> false, sem OOB
    {
        uint8_t buf[15] = {0}; // falta o ultimo byte de directionX (offset 15)
        buf[0] = 0x01; buf[1] = 1; buf[2] = 8;
        FxEffect e;
        CHECK(fxDecodeSetEffect(buf, sizeof(buf), e) == false);
    }

    // ---- Set Condition (0x03): cpOffset/coeffs negativos (s16) ----
    {
        uint8_t buf[15] = {0};
        buf[0] = 0x03;
        buf[1] = 5; // block
        buf[2] = 0; // paramBlockOffset
        // cpOffset = -1000 -> 0xFC18
        buf[3] = 0x18; buf[4] = 0xFC;
        // posCoeff = -2000 -> 0xF830
        buf[5] = 0x30; buf[6] = 0xF8;
        // negCoeff = -3000 -> 0xF448
        buf[7] = 0x48; buf[8] = 0xF4;
        // posSat = 30000
        buf[9] = 0x30; buf[10] = 0x75;
        // negSat = 31000
        buf[11] = 0x18; buf[12] = 0x79;
        // deadBand = 100
        buf[13] = 0x64; buf[14] = 0x00;

        FxEffect e;
        bool ok = fxDecodeCondition(buf, sizeof(buf), e);
        CHECK(ok);
        CHECK(e.block == 5);
        CHECK(e.centerOffset == -1000);
        CHECK(e.posCoeff == -2000);
        CHECK(e.negCoeff == -3000);
        CHECK(e.posSat == 30000);
        CHECK(e.negSat == 31000);
        CHECK(e.deadBand == 100);
    }

    // Set Condition: buffer curto -> false
    {
        uint8_t buf[10] = {0};
        buf[0] = 0x03; buf[1] = 1;
        FxEffect e;
        CHECK(fxDecodeCondition(buf, sizeof(buf), e) == false);
    }

    // ---- Set Periodic (0x04): period 32-bit > 65535 ----
    {
        uint8_t buf[12] = {0};
        buf[0] = 0x04;
        buf[1] = 7; // block
        buf[2] = 0x88; buf[3] = 0x13; // magnitude = 5000
        // offset = -500 -> 0xFE0C
        buf[4] = 0x0C; buf[5] = 0xFE;
        buf[6] = 0x90; buf[7] = 0x01; // phase = 400 (4.00 deg)
        // period = 100000 (> 65535) -> LE u32
        buf[8] = 0xA0; buf[9] = 0x86; buf[10] = 0x01; buf[11] = 0x00;

        FxEffect e;
        bool ok = fxDecodePeriodic(buf, sizeof(buf), e);
        CHECK(ok);
        CHECK(e.block == 7);
        CHECK(e.magnitude16 == 5000);
        CHECK(e.offset == -500);
        CHECK(e.phase == 400);
        CHECK(e.period == 100000); // > 65535 — comprova leitura de 4 bytes (u32), não 2
    }

    // Set Periodic: buffer curto -> false
    {
        uint8_t buf[8] = {0};
        buf[0] = 0x04; buf[1] = 1;
        FxEffect e;
        CHECK(fxDecodePeriodic(buf, sizeof(buf), e) == false);
    }

    // ---- Set Envelope (0x02): attackTime/fadeTime 32-bit ----
    {
        uint8_t buf[14] = {0};
        buf[0] = 0x02;
        buf[1] = 2; // block
        buf[2] = 0x64; buf[3] = 0x00; // attackLevel = 100
        buf[4] = 0xC8; buf[5] = 0x00; // fadeLevel = 200
        // attackTime = 70000 (> 65535)
        buf[6] = 0x70; buf[7] = 0x11; buf[8] = 0x01; buf[9] = 0x00;
        // fadeTime = 80000
        buf[10] = 0x80; buf[11] = 0x38; buf[12] = 0x01; buf[13] = 0x00;

        FxEffect e;
        bool ok = fxDecodeEnvelope(buf, sizeof(buf), e);
        CHECK(ok);
        CHECK(e.block == 2);
        CHECK(e.attackLevel == 100);
        CHECK(e.fadeLevel == 200);
        CHECK(e.attackMs == 70000);
        CHECK(e.fadeMs == 80000);
    }

    // Set Envelope: buffer curto -> false
    {
        uint8_t buf[9] = {0};
        buf[0] = 0x02; buf[1] = 1;
        FxEffect e;
        CHECK(fxDecodeEnvelope(buf, sizeof(buf), e) == false);
    }

    // ---- Set Ramp (0x06): start/end negativos (s16) ----
    {
        uint8_t buf[6] = {0};
        buf[0] = 0x06;
        buf[1] = 9; // block
        // startLevel = -1500 -> 0xFA24
        buf[2] = 0x24; buf[3] = 0xFA;
        // endLevel = 1500 -> 0x05DC
        buf[4] = 0xDC; buf[5] = 0x05;

        FxEffect e;
        bool ok = fxDecodeRamp(buf, sizeof(buf), e);
        CHECK(ok);
        CHECK(e.block == 9);
        CHECK(e.rampStart == -1500);
        CHECK(e.rampEnd == 1500);
    }

    // Set Ramp: buffer curto -> false
    {
        uint8_t buf[4] = {0};
        buf[0] = 0x06; buf[1] = 1;
        FxEffect e;
        CHECK(fxDecodeRamp(buf, sizeof(buf), e) == false);
    }

    // ---- Set Constant (0x05): magnitude s16 ----
    {
        uint8_t buf[4] = {0};
        buf[0] = 0x05;
        buf[1] = 4; // block
        // magnitude = -1000 -> 0xFC18
        buf[2] = 0x18; buf[3] = 0xFC;

        FxEffect e;
        bool ok = fxDecodeConstant(buf, sizeof(buf), e);
        CHECK(ok);
        CHECK(e.block == 4);
        CHECK(e.magnitude == -1000);
    }

    // Set Constant: buffer curto -> false
    {
        uint8_t buf[3] = {0};
        buf[0] = 0x05; buf[1] = 1;
        FxEffect e;
        CHECK(fxDecodeConstant(buf, sizeof(buf), e) == false);
    }

    // ---- nullptr/len==0 nunca trava ----
    {
        FxEffect e;
        CHECK(fxDecodeSetEffect(nullptr, 0, e) == false);
        CHECK(fxDecodeCondition(nullptr, 0, e) == false);
        CHECK(fxDecodePeriodic(nullptr, 0, e) == false);
        CHECK(fxDecodeEnvelope(nullptr, 0, e) == false);
        CHECK(fxDecodeRamp(nullptr, 0, e) == false);
        CHECK(fxDecodeConstant(nullptr, 0, e) == false);
    }

    std::printf("%s  — %d checks, %d fail(s)\n", g_fails ? "FALHOU" : "OK", g_checks, g_fails);
    return g_fails ? 1 : 0;
}
