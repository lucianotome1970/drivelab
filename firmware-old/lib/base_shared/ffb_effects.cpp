// ============================================================================
//  DriveLab Firmware
//  ffb_effects.cpp — implementação do parser puro de OUT reports PID de
//  efeitos (Set Effect / Envelope / Condition / Periodic / Constant / Ramp).
//  Layouts conforme OpenFFBoard (LE, byte0 = ReportID). Todo decode valida
//  `len` ANTES de indexar — nunca lê além do buffer.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include "ffb_effects.h"

namespace {
FxType fxTypeFromWire(uint8_t v) {
    if (v <= 11) return static_cast<FxType>(v);
    return FxType::None;
}
} // namespace

// Set Effect (0x01): block@1, type@2, duration@3:u16, startDelay@9:u16,
// gain@11:u8, enableAxis@13:u8, directionX@14:u16.
// Maior offset lido é 14..15 -> precisa len >= 16.
bool fxDecodeSetEffect(const uint8_t* buf, uint16_t len, FxEffect& e) {
    if (buf == nullptr || len < 16) return false;

    e.block = buf[1];
    e.type = fxTypeFromWire(buf[2]);
    uint16_t duration = fxU16(&buf[3]);
    e.durationMs = duration; // 0 == infinito (armazena 0)
    e.startMs = fxU16(&buf[9]);
    e.gain = buf[11];
    e.directionCentideg = (int16_t)fxU16(&buf[14]);
    return true;
}

// Set Condition (0x03): block@1, paramBlockOffset@2, cpOffset@3:s16,
// posCoeff@5:s16, negCoeff@7:s16, posSat@9:u16, negSat@11:u16, deadBand@13:u16.
// Maior offset lido é 13..14 -> precisa len >= 15.
bool fxDecodeCondition(const uint8_t* buf, uint16_t len, FxEffect& e) {
    if (buf == nullptr || len < 15) return false;

    e.block = buf[1];
    e.centerOffset = fxS16(&buf[3]);
    e.posCoeff = fxS16(&buf[5]);
    e.negCoeff = fxS16(&buf[7]);
    e.posSat = fxU16(&buf[9]);
    e.negSat = fxU16(&buf[11]);
    e.deadBand = fxU16(&buf[13]);
    return true;
}

// Set Periodic (0x04): block@1, magnitude@2:u16, offset@4:s16, phase@6:u16,
// period@8:u32. Maior offset lido é 8..11 -> precisa len >= 12.
bool fxDecodePeriodic(const uint8_t* buf, uint16_t len, FxEffect& e) {
    if (buf == nullptr || len < 12) return false;

    e.block = buf[1];
    e.magnitude16 = fxU16(&buf[2]);
    e.offset = fxS16(&buf[4]);
    e.phase = fxU16(&buf[6]);
    e.period = fxU32(&buf[8]);
    return true;
}

// Set Envelope (0x02): block@1, attackLevel@2:u16, fadeLevel@4:u16,
// attackTime@6:u32, fadeTime@10:u32. Maior offset lido é 10..13 -> precisa len >= 14.
bool fxDecodeEnvelope(const uint8_t* buf, uint16_t len, FxEffect& e) {
    if (buf == nullptr || len < 14) return false;

    e.block = buf[1];
    e.attackLevel = fxU16(&buf[2]);
    e.fadeLevel = fxU16(&buf[4]);
    e.attackMs = fxU32(&buf[6]);
    e.fadeMs = fxU32(&buf[10]);
    return true;
}

// Set Ramp (0x06): block@1, startLevel@2:s16, endLevel@4:s16.
// Maior offset lido é 4..5 -> precisa len >= 6.
bool fxDecodeRamp(const uint8_t* buf, uint16_t len, FxEffect& e) {
    if (buf == nullptr || len < 6) return false;

    e.block = buf[1];
    e.rampStart = fxS16(&buf[2]);
    e.rampEnd = fxS16(&buf[4]);
    return true;
}

// Set Constant (0x05): block@1, magnitude@2:s16.
// Maior offset lido é 2..3 -> precisa len >= 4.
bool fxDecodeConstant(const uint8_t* buf, uint16_t len, FxEffect& e) {
    if (buf == nullptr || len < 4) return false;

    e.block = buf[1];
    e.magnitude = fxS16(&buf[2]);
    return true;
}
