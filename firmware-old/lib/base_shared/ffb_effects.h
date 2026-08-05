// ============================================================================
//  DriveLab Firmware
//  ffb_effects.h — FxType/FxEffect + parser puro de OUT reports PID
//  (Set Effect / Set Envelope / Set Condition / Set Periodic / Set Constant /
//  Set Ramp), host-testável, sem dependência de Arduino/USB.
//  Layouts conforme OpenFFBoard (LE, byte0 = ReportID).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cstdint>

// Valores on-wire do campo effectType do Set Effect report.
enum class FxType : uint8_t {
    None = 0,
    Constant = 1,
    Ramp = 2,
    Square = 3,
    Sine = 4,
    Triangle = 5,
    SawtoothUp = 6,
    SawtoothDown = 7,
    Spring = 8,
    Damper = 9,
    Inertia = 10,
    Friction = 11,
};

// Estado agregado de um efeito (um "slot"/effectBlockIndex), acumulado a
// partir de múltiplos reports PID (Set Effect, Set Envelope, Set Condition,
// Set Periodic, Set Ramp, Set Constant). Cada decode escreve só os campos
// referentes àquele report, deixando os demais intocados.
struct FxEffect {
    FxType type = FxType::None;
    bool active = false;
    uint8_t block = 0;         // effectBlockIndex do report que populou este slot

    // Set Effect (0x01)
    uint32_t startMs = 0;
    uint32_t durationMs = 0;   // 0 == infinito
    uint8_t gain = 255;
    int16_t directionCentideg = 0;

    // Set Constant (0x05) / Set Ramp (0x06)
    int16_t magnitude = 0;
    int16_t rampStart = 0;
    int16_t rampEnd = 0;

    // Set Periodic (0x04)
    int16_t offset = 0;
    uint32_t period = 0;   // on-wire é u32 (não cabe em u16 — ex.: 100000)
    uint16_t phase = 0;
    uint16_t magnitude16 = 0; // magnitude do Set Periodic (u16), separado de `magnitude` (s16 do Constant/Ramp)

    // Set Condition (0x03)
    int16_t centerOffset = 0;
    int16_t posCoeff = 0;
    int16_t negCoeff = 0;
    uint16_t posSat = 32767;
    uint16_t negSat = 32767;
    uint16_t deadBand = 0;

    // Set Envelope (0x02)
    uint16_t attackLevel = 0;
    uint16_t fadeLevel = 0;
    uint32_t attackMs = 0;
    uint32_t fadeMs = 0;
};

// Helpers little-endian (bytes já dentro de `buf`, sem checagem de limite —
// quem chama garante `len` suficiente antes de usar).
inline uint16_t fxU16(const uint8_t* p) { return (uint16_t)p[0] | ((uint16_t)p[1] << 8); }
inline int16_t  fxS16(const uint8_t* p) { return (int16_t)fxU16(p); }
inline uint32_t fxU32(const uint8_t* p) { return fxU16(p) | ((uint32_t)fxU16(p + 2) << 16); }

// Cada função decodifica um OUT report PID e escreve os campos correspondentes
// em `e` (demais campos de `e` ficam intocados). Retorna true se `len` era
// suficiente para todos os campos do report e a decodificação ocorreu; nunca
// indexa além de `len`.
bool fxDecodeSetEffect(const uint8_t* buf, uint16_t len, FxEffect& e);
bool fxDecodeCondition(const uint8_t* buf, uint16_t len, FxEffect& e);
bool fxDecodePeriodic(const uint8_t* buf, uint16_t len, FxEffect& e);
bool fxDecodeEnvelope(const uint8_t* buf, uint16_t len, FxEffect& e);
bool fxDecodeRamp(const uint8_t* buf, uint16_t len, FxEffect& e);
bool fxDecodeConstant(const uint8_t* buf, uint16_t len, FxEffect& e);
