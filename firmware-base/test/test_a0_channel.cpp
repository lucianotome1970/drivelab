// ============================================================================
//  DriveLab Firmware
//  test_a0_channel.cpp — Teste de HOST do framing puro do canal A0
//  (A0Channel::buildDeviceStatePayload): versão de firmware, flags e
//  McuTempC no payload DeviceState (0x21), espelhando byte-a-byte
//  app/DriveLab.Core/Protocol/BaseState.cs. Roda sem placa — a0_channel.h
//  não depende de Arduino/TinyUSB (ver comentário no topo do header).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Roda sem placa nenhuma: firmware-base/test/run.sh

#include "../lib/base_usb/a0_channel.h"
#include "../lib/base_shared/fw_signature.h"

#include <cstdio>

static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

int main()
{
    uint8_t out[kA0PayloadLen];
    // Preenche com lixo p/ garantir que buildDeviceStatePayload realmente
    // zera o resto (não só escreve os campos conhecidos por cima de zeros
    // já existentes).
    for (uint16_t i = 0; i < kA0PayloadLen; ++i)
    {
        out[i] = 0xAA;
    }

    uint16_t len = A0Channel::buildDeviceStatePayload(out, 28);

    CHECK(len == kA0PayloadLen);
    CHECK(len == 63);

    // [0..3] = versão (ReleaseType=0/dev, Major, Minor, Patch — DRVLAB_FW_VER_*)
    CHECK(out[0] == 0);
    CHECK(out[1] == DRVLAB_FW_VER_MAJOR);
    CHECK(out[2] == DRVLAB_FW_VER_MINOR);
    CHECK(out[3] == DRVLAB_FW_VER_PATCH);

    // [4] flags — nenhuma definida ainda.
    CHECK(out[4] == 0);

    // [18] McuTempC — o único sensor real do M0.5.
    CHECK(out[18] == 28);

    // Resto (posição/ângulo/torque/corrente/FET/erro/bus/motor/reservado):
    // tudo 0 — adiado pro M1 (sem motor/sensores ainda).
    for (uint16_t i = 5; i < kA0PayloadLen; ++i)
    {
        if (i == 18) continue;
        CHECK(out[i] == 0);
    }

    // Temperatura negativa (sbyte) — checa que o cast não estoura.
    uint8_t out2[kA0PayloadLen];
    A0Channel::buildDeviceStatePayload(out2, -5);
    CHECK(static_cast<int8_t>(out2[18]) == -5);

    // [19] Clipping — nível de corte do FFB (0-255), byte novo do medidor.
    uint8_t out3[kA0PayloadLen];
    A0Channel::buildDeviceStatePayload(out3, 28, 200);
    CHECK(out3[19] == 200);
    CHECK(out3[18] == 28);

    // [5..6] Position (int16 LE) + [7..8] AngleDeciDeg (int16 LE) — set-center do volante.
    uint8_t out4[kA0PayloadLen];
    A0Channel::buildDeviceStatePayload(out4, 28, 0, /*position=*/-3000, /*angleDeciDeg=*/900);
    CHECK(static_cast<int16_t>(out4[5] | (out4[6] << 8)) == -3000); // negativo (LE) intacto
    CHECK(static_cast<int16_t>(out4[7] | (out4[8] << 8)) == 900);   // 90.0°
    CHECK(out4[19] == 0);

    // [15..16] BusVoltageMv (uint16 LE, mV) — tensão do barramento.
    uint8_t out5[kA0PayloadLen];
    A0Channel::buildDeviceStatePayload(out5, 28, 0, 0, 0, /*busVoltageMv=*/56000);
    CHECK(static_cast<uint16_t>(out5[15] | (out5[16] << 8)) == 56000); // 56.000 V
    CHECK(out5[18] == 28);                                             // mcuTemp intacto

    // angleDeciDegFromRad: rad → 0.1°, com saturação em int16.
    CHECK(A0Channel::angleDeciDegFromRad(0.0f) == 0);
    CHECK(A0Channel::angleDeciDegFromRad(3.14159265358979323846f) == 1800);   // π rad = 180.0°
    CHECK(A0Channel::angleDeciDegFromRad(-3.14159265358979323846f) == -1800); // simétrico
    CHECK(A0Channel::angleDeciDegFromRad(1.5707963f) == 900);                 // π/2 = 90.0°
    CHECK(A0Channel::angleDeciDegFromRad(1000.0f) == 32767);                  // satura (int16 max)
    CHECK(A0Channel::angleDeciDegFromRad(-1000.0f) == -32768);               // satura (int16 min)

    std::printf("a0_channel: %d checks, %d fails\n", g_checks, g_fails);
    return g_fails == 0 ? 0 : 1;
}
