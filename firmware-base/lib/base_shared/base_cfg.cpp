// ============================================================================
//  DriveLab Firmware
//  base_cfg.cpp — Implementação do modelo BaseCfg: seed de defaults e
//  decode/encode little-endian por tipo (mirror de decodeValue/sendSettingValue
//  em firmware-pedal/src/main.cpp).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include "base_cfg.h"

#include <cstring>

uint8_t baseTypeForField(uint8_t id) {
    switch (id) {
        case BID_MOTION_RANGE:
        case BID_ENCODER_CPR:
        case BID_OUTPUT_FILTER_HZ:
            return BT_UINT16;
        case BID_FORCE_DIRECTION:
        case BID_ENCODER_DIRECTION:
            return BT_INT8;
        case BID_CURRENT_P:
        case BID_CURRENT_I:
            return BT_FLOAT;
        case BID_SOFT_STOP_RANGE:
        case BID_SOFT_STOP_STRENGTH:
        case BID_TOTAL_STRENGTH:
        case BID_SPRING_STRENGTH:
        case BID_DAMPER_STRENGTH:
        case BID_STATIC_DAMPING:
        case BID_MAX_TORQUE_LIMIT:
        case BID_POLE_PAIRS:
        case BID_CALIBRATION_CURRENT:
        case BID_POSITION_SMOOTHING:
        case BID_POWER_LIMIT:
        case BID_BRAKING_LIMIT:
        case BID_ENCODER_TYPE:
        case BID_RECONSTRUCTION_STEPS:
        case BID_RECONSTRUCTION_LPF:
        case BID_OSC_GUARD_ENABLE:
        case BID_ENDSTOP_DAMPING:
        case BID_LINEARITY:
        case BID_COGGING_ENABLE:
        case BID_SLEW_RATE:
        case BID_BUS_NOMINAL_V:
        case BID_FFB_CURVE_0:
        case BID_FFB_CURVE_1:
        case BID_FFB_CURVE_2:
        case BID_FFB_CURVE_3:
        case BID_FFB_CURVE_4:
            return BT_UINT8;
        default:
            return 0xFF;
    }
}

// Ponteiro para o campo de `c` correspondente a `id`. nullptr se id desconhecido.
// (helper interno — evita duplicar o switch id->campo entre read e write)
static void* fieldPtr(BaseCfg& c, uint8_t id) {
    switch (id) {
        case BID_MOTION_RANGE:         return &c.motionRange;
        case BID_SOFT_STOP_RANGE:      return &c.softStopRange;
        case BID_SOFT_STOP_STRENGTH:   return &c.softStopStrength;
        case BID_TOTAL_STRENGTH:       return &c.totalStrength;
        case BID_SPRING_STRENGTH:      return &c.springStrength;
        case BID_DAMPER_STRENGTH:      return &c.damperStrength;
        case BID_STATIC_DAMPING:       return &c.staticDamping;
        case BID_MAX_TORQUE_LIMIT:     return &c.maxTorqueLimit;
        case BID_FORCE_DIRECTION:      return &c.forceDirection;
        case BID_ENCODER_DIRECTION:    return &c.encoderDirection;
        case BID_ENCODER_CPR:          return &c.encoderCpr;
        case BID_POLE_PAIRS:           return &c.polePairs;
        case BID_CURRENT_P:            return &c.currentP;
        case BID_CURRENT_I:            return &c.currentI;
        case BID_CALIBRATION_CURRENT:  return &c.calibrationCurrent;
        case BID_POSITION_SMOOTHING:   return &c.positionSmoothing;
        case BID_POWER_LIMIT:          return &c.powerLimit;
        case BID_BRAKING_LIMIT:        return &c.brakingLimit;
        case BID_ENCODER_TYPE:         return &c.encoderType;
        case BID_RECONSTRUCTION_STEPS: return &c.reconstructionSteps;
        case BID_RECONSTRUCTION_LPF:   return &c.reconstructionLpf;
        case BID_OUTPUT_FILTER_HZ:     return &c.outputFilterHz;
        case BID_OSC_GUARD_ENABLE:     return &c.oscGuardEnable;
        case BID_ENDSTOP_DAMPING:      return &c.endstopDamping;
        case BID_LINEARITY:            return &c.linearity;
        case BID_COGGING_ENABLE:       return &c.coggingEnable;
        case BID_SLEW_RATE:            return &c.slewRate;
        case BID_BUS_NOMINAL_V:        return &c.busNominalV;
        case BID_FFB_CURVE_0:          return &c.ffbCurve[0];
        case BID_FFB_CURVE_1:          return &c.ffbCurve[1];
        case BID_FFB_CURVE_2:          return &c.ffbCurve[2];
        case BID_FFB_CURVE_3:          return &c.ffbCurve[3];
        case BID_FFB_CURVE_4:          return &c.ffbCurve[4];
        default:                       return nullptr;
    }
}

int baseReadField(const BaseCfg& c, uint8_t id, uint8_t* outType, uint8_t* outBuf) {
    uint8_t type = baseTypeForField(id);
    if (type == 0xFF) return 0;

    void* p = fieldPtr(const_cast<BaseCfg&>(c), id);
    if (p == nullptr) return 0;

    switch (type) {
        case BT_UINT8: {
            outBuf[0] = *static_cast<uint8_t*>(p);
            *outType = type;
            return 1;
        }
        case BT_INT8: {
            outBuf[0] = static_cast<uint8_t>(*static_cast<int8_t*>(p));
            *outType = type;
            return 1;
        }
        case BT_UINT16: {
            uint16_t v = *static_cast<uint16_t*>(p);
            outBuf[0] = v & 0xFF;
            outBuf[1] = (v >> 8) & 0xFF;
            *outType = type;
            return 2;
        }
        case BT_INT16: {
            int16_t v = *static_cast<int16_t*>(p);
            uint16_t u = static_cast<uint16_t>(v);
            outBuf[0] = u & 0xFF;
            outBuf[1] = (u >> 8) & 0xFF;
            *outType = type;
            return 2;
        }
        case BT_FLOAT: {
            std::memcpy(outBuf, p, 4);
            *outType = type;
            return 4;
        }
        default:
            return 0;
    }
}

void baseWriteField(BaseCfg& c, uint8_t id, uint8_t type, const uint8_t* buf, uint16_t len) {
    uint8_t expected = baseTypeForField(id);
    if (expected == 0xFF || type != expected) return;

    void* p = fieldPtr(c, id);
    if (p == nullptr) return;

    switch (type) {
        case BT_UINT8:
            if (len < 1) return;
            *static_cast<uint8_t*>(p) = buf[0];
            return;
        case BT_INT8:
            if (len < 1) return;
            *static_cast<int8_t*>(p) = static_cast<int8_t>(buf[0]);
            return;
        case BT_UINT16:
            if (len < 2) return;
            *static_cast<uint16_t*>(p) = static_cast<uint16_t>(buf[0] | (buf[1] << 8));
            return;
        case BT_INT16:
            if (len < 2) return;
            *static_cast<int16_t*>(p) = static_cast<int16_t>(buf[0] | (buf[1] << 8));
            return;
        case BT_FLOAT:
            if (len < 4) return;
            std::memcpy(p, buf, 4);
            return;
        default:
            return;
    }
}

void baseSeedDefaults(BaseCfg& c) {
    c.motionRange = 900;
    c.softStopRange = 5;
    c.softStopStrength = 80;
    c.totalStrength = 100;
    c.springStrength = 0;
    c.damperStrength = 10;
    c.staticDamping = 5;
    c.maxTorqueLimit = 80;
    c.forceDirection = 1;
    c.encoderDirection = 1;
    c.encoderCpr = 4000;   // Omron E6B2-CWZ6C 1000 P/R × 4 (quadratura)
    c.polePairs = 15;
    c.currentP = 0.05f;
    c.currentI = 10.0f;
    c.calibrationCurrent = 30;
    c.positionSmoothing = 0;
    c.powerLimit = 100;
    c.brakingLimit = 100;
    c.encoderType = 0;
    c.reconstructionSteps = 0;
    c.reconstructionLpf = 0;
    c.outputFilterHz = 0;
    c.oscGuardEnable = 0;
    c.endstopDamping = 0;
    c.linearity = 100;
    c.coggingEnable = 0;
    c.slewRate = 0;
    c.busNominalV = 56;   // variante 56V da placa; o usuário ajusta conforme a fonte
    // Curva de resposta da força: linear por padrão (identidade — não altera o feel de quem não mexer).
    c.ffbCurve[0] = 0; c.ffbCurve[1] = 25; c.ffbCurve[2] = 50; c.ffbCurve[3] = 75; c.ffbCurve[4] = 100;
}
