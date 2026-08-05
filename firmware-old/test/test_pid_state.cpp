// ============================================================================
//  DriveLab Firmware
//  test_pid_state.cpp — Teste de HOST do builder puro do PID State Report
//  (buildPidStateByte): confere a ordem dos bits do bitfield de status FFB
//  contra o layout do descritor HID PID (bloco 0x85,0x02). Roda sem placa —
//  pid_state.h não depende de Arduino/TinyUSB.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Roda sem placa nenhuma: firmware-base/test/run.sh

#include "../lib/base_usb/pid_state.h"

#include <cstdio>

static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

int main()
{
    // Caso do brief: não pausado, atuadores habilitados, safety ok, sem
    // override, com power -> bits 1,2,4 = 0x16.
    CHECK(buildPidStateByte(false, true, true, false, true) == 0x16);

    // Todos falsos -> 0.
    CHECK(buildPidStateByte(false, false, false, false, false) == 0x00);

    // Todos verdadeiros -> bits 0..4 = 0x1F (padding 5..7 nunca setado).
    CHECK(buildPidStateByte(true, true, true, true, true) == 0x1F);

    // Cada bit isolado.
    CHECK(buildPidStateByte(true, false, false, false, false) == 0x01);  // Device Paused
    CHECK(buildPidStateByte(false, true, false, false, false) == 0x02);  // Actuators Enabled
    CHECK(buildPidStateByte(false, false, true, false, false) == 0x04);  // Safety Switch
    CHECK(buildPidStateByte(false, false, false, true, false) == 0x08);  // Actuator Override
    CHECK(buildPidStateByte(false, false, false, false, true) == 0x10);  // Actuator Power

    std::printf("pid_state: %d checks, %d fails\n", g_checks, g_fails);
    return g_fails == 0 ? 0 : 1;
}
