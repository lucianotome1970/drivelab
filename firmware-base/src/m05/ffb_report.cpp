// ============================================================================
//  DriveLab Firmware
//  ffb_report.cpp — Implementação do parser puro de OUT report PID.
//  Layout de bytes confirmado em firmware-base/include/ffb_hid_descriptor.h
//  (descritor gerado a partir do OpenFFBoard):
//    Set Constant Force (RID 0x05): buf[0]=ReportID, buf[1]=Effect Block
//      Index (1 byte), buf[2..3]=Magnitude (int16 little-endian, -32767..32767).
//    Effect Operation (RID 0x0A): buf[0]=ReportID, buf[1]=Effect Block
//      Index (1 byte), buf[2]=Effect Operation selector (1 byte,
//      1=start/2=start-solo/3=stop), buf[3]=Loop Count (não usado aqui).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================

#include "ffb_report.h"
#include "../../include/ffb_hid_descriptor.h"

FfbOut ffb_parse_out(const uint8_t* buf, uint16_t len) {
    FfbOut out;

    if (buf == nullptr || len < 1) {
        return out; // FFB_UNKNOWN por padrão
    }

    switch (buf[0]) {
        case RID_PID_SET_CONSTANT_FORCE:
            if (len < 4) return out;
            out.type = FFB_SET_CONSTANT_FORCE;
            out.effectBlock = buf[1];
            out.constantForce = static_cast<int16_t>(
                static_cast<uint16_t>(buf[2]) | (static_cast<uint16_t>(buf[3]) << 8));
            return out;

        case RID_PID_EFFECT_OPERATION:
            if (len < 3) return out;
            out.type = FFB_EFFECT_OPERATION;
            out.effectBlock = buf[1];
            out.op = buf[2];
            return out;

        case RID_PID_BLOCK_LOAD:
            out.type = FFB_BLOCK_LOAD;
            return out;

        case RID_PID_DEVICE_CONTROL:
            out.type = FFB_DEVICE_CONTROL;
            return out;

        case RID_PID_SET_EFFECT:
            out.type = FFB_SET_EFFECT;
            return out;

        default:
            return out; // FFB_UNKNOWN
    }
}
