// ============================================================================
//  DriveLab Firmware
//  base_cfg.h — Modelo puro dos settings da base (força, encoder, corrente,
//  etc.), host-testável, sem dependência de Arduino/USB. Espelha 1:1 o
//  enum BaseSettingId e o BaseSettingsSchema do app (DriveLab.Core.Settings).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cstdint>

// IDs dos settings da base — devem casar byte-a-byte com
// app/DriveLab.Core/Settings/BaseSettingId.cs (enum BaseSettingId : byte).
#define BID_MOTION_RANGE          0
#define BID_SOFT_STOP_RANGE       1
#define BID_SOFT_STOP_STRENGTH    2
#define BID_TOTAL_STRENGTH        3
#define BID_SPRING_STRENGTH       4
#define BID_DAMPER_STRENGTH       5
#define BID_STATIC_DAMPING        6
#define BID_MAX_TORQUE_LIMIT      7
#define BID_FORCE_DIRECTION       8
#define BID_ENCODER_DIRECTION     9
#define BID_ENCODER_CPR           10
#define BID_POLE_PAIRS            11
#define BID_CURRENT_P             12
#define BID_CURRENT_I             13
#define BID_CALIBRATION_CURRENT   14
#define BID_POSITION_SMOOTHING    15
#define BID_POWER_LIMIT           16
#define BID_BRAKING_LIMIT         17
#define BID_ENCODER_TYPE          18
#define BID_RECONSTRUCTION_STEPS  19
#define BID_RECONSTRUCTION_LPF    20
#define BID_OUTPUT_FILTER_HZ      21
#define BID_OSC_GUARD_ENABLE      22
#define BID_ENDSTOP_DAMPING       23
#define BID_LINEARITY             24
#define BID_COGGING_ENABLE        25
#define BID_SLEW_RATE             26
#define BID_BUS_NOMINAL_V         27
#define BID_FFB_CURVE_0           28
#define BID_FFB_CURVE_1           29
#define BID_FFB_CURVE_2           30
#define BID_FFB_CURVE_3           31
#define BID_FFB_CURVE_4           32

// Tipos de dado — devem casar com app/DriveLab.Core/Settings/SettingType.cs
// (enum SettingType : byte).
#define BT_UINT8   0
#define BT_INT8    1
#define BT_UINT16  2
#define BT_INT16   3
#define BT_FLOAT   4

// Um campo por BaseSettingId, tipo conforme BaseSettingsSchema.cs.
struct BaseCfg {
    uint16_t motionRange;         // BID_MOTION_RANGE          UInt16
    uint8_t  softStopRange;       // BID_SOFT_STOP_RANGE       UInt8
    uint8_t  softStopStrength;    // BID_SOFT_STOP_STRENGTH    UInt8
    uint8_t  totalStrength;       // BID_TOTAL_STRENGTH        UInt8
    uint8_t  springStrength;      // BID_SPRING_STRENGTH       UInt8
    uint8_t  damperStrength;      // BID_DAMPER_STRENGTH       UInt8
    uint8_t  staticDamping;       // BID_STATIC_DAMPING        UInt8
    uint8_t  maxTorqueLimit;      // BID_MAX_TORQUE_LIMIT      UInt8
    int8_t   forceDirection;      // BID_FORCE_DIRECTION       Int8
    int8_t   encoderDirection;    // BID_ENCODER_DIRECTION     Int8
    uint16_t encoderCpr;          // BID_ENCODER_CPR           UInt16
    uint8_t  polePairs;           // BID_POLE_PAIRS            UInt8
    float    currentP;            // BID_CURRENT_P             Float
    float    currentI;            // BID_CURRENT_I             Float
    uint8_t  calibrationCurrent;  // BID_CALIBRATION_CURRENT   UInt8
    uint8_t  positionSmoothing;   // BID_POSITION_SMOOTHING    UInt8
    uint8_t  powerLimit;          // BID_POWER_LIMIT           UInt8
    uint8_t  brakingLimit;        // BID_BRAKING_LIMIT         UInt8
    uint8_t  encoderType;         // BID_ENCODER_TYPE          UInt8
    uint8_t  reconstructionSteps; // BID_RECONSTRUCTION_STEPS  UInt8 (0=auto)
    uint8_t  reconstructionLpf;   // BID_RECONSTRUCTION_LPF    UInt8 0..100
    uint16_t outputFilterHz;      // BID_OUTPUT_FILTER_HZ      UInt16 (0=off)
    uint8_t  oscGuardEnable;      // BID_OSC_GUARD_ENABLE      UInt8 0/1
    uint8_t  endstopDamping;      // BID_ENDSTOP_DAMPING       UInt8 0..100
    uint8_t  linearity;           // BID_LINEARITY             UInt8 50..200 (÷100)
    uint8_t  coggingEnable;       // BID_COGGING_ENABLE        UInt8 0/1
    uint8_t  slewRate;            // BID_SLEW_RATE             UInt8 0..100
    uint8_t  busNominalV;         // BID_BUS_NOMINAL_V         UInt8 (V nominal da fonte; deriva a janela busMin/max/over)
    uint8_t  ffbCurve[5];         // BID_FFB_CURVE_0..4        UInt8 0..100 — curva de resposta da força (5 pontos)
};

// Retorna o SettingType (0..4, ver BT_*) do campo `id`, ou 0xFF se id desconhecido.
uint8_t baseTypeForField(uint8_t id);

// Lê o campo `id` de `c`, grava o tipo em `*outType` e o valor em LE em
// `outBuf` (buffer do chamador, deve ter >= 4 bytes). Retorna o número de
// bytes escritos (1, 2 ou 4), ou 0 se `id` for desconhecido (outBuf/outType
// não são tocados nesse caso).
int baseReadField(const BaseCfg& c, uint8_t id, uint8_t* outType, uint8_t* outBuf);

// Decodifica `buf` (LE, `len` bytes) conforme `type` e grava no campo `id`
// de `c`. Ignora silenciosamente id desconhecido ou `len` menor que o
// tamanho esperado pelo tipo (sem crash, sem leitura fora de `buf`).
void baseWriteField(BaseCfg& c, uint8_t id, uint8_t type, const uint8_t* buf, uint16_t len);

// Preenche `c` com os defaults do schema (BaseSettingsSchema.cs).
void baseSeedDefaults(BaseCfg& c);
