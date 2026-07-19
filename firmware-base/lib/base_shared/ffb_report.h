// ============================================================================
//  DriveLab Firmware
//  ffb_report.h — Parser puro de OUT report PID (Set Effect / Set Constant
//  Force / Effect Operation / Block Load / Device Control), host-testável,
//  sem dependência de Arduino/USB.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cstdint>

enum FfbReportType {
    FFB_SET_EFFECT,
    FFB_SET_CONSTANT_FORCE,
    FFB_EFFECT_OPERATION,
    FFB_BLOCK_LOAD,
    FFB_DEVICE_CONTROL,
    FFB_UNKNOWN,
};

struct FfbOut {
    FfbReportType type = FFB_UNKNOWN;
    uint8_t effectBlock = 0;
    int16_t constantForce = 0;
    uint8_t op = 0;
};

// Decodifica um OUT report (host->device) recebido pelo endpoint HID.
// buf/len vêm sempre do sentido Output — IDs numéricos que colidem com
// reports de Input (ex.: 0x01 = Joystick no Input, mas Set Effect no
// Output) são tratados aqui como Output. Retorna FFB_UNKNOWN para IDs
// desconhecidos ou reports curtos demais para os campos exigidos —
// nunca indexa além de `len`.
FfbOut ffb_parse_out(const uint8_t* buf, uint16_t len);
