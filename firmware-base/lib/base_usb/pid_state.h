// ============================================================================
//  DriveLab Firmware
//  pid_state.h — Builder PURO do byte do PID State Report (Input, RID 0x02).
//  Monta o bitfield de status do dispositivo FFB conforme o descritor HID PID
//  (ver include/ffb_hid_descriptor.h, bloco 0x85,0x02): 5 bits de status +
//  3 bits de padding constante. A ordem dos bits segue a ordem das usages no
//  descritor (0x9F Device Paused, 0xA0 Actuators Enabled, 0xA4 Safety Switch,
//  0xA6 Actuator Override Switch, 0x94 Actuator Power), Report Size 1/Count 5.
//
//  Header DELIBERADAMENTE sem dependências de Arduino/TinyUSB — mesmo padrão de
//  a0_channel.h — p/ que buildPidStateByte() seja testável no host (ver
//  test/test_pid_state.cpp) sem placa.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cstdint>

// Layout confirmado contra include/ffb_hid_descriptor.h (bloco 0x85,0x02):
//   bit0 = Device Paused              (usage 0x9F)
//   bit1 = Actuators Enabled          (usage 0xA0)
//   bit2 = Safety Switch              (usage 0xA4)
//   bit3 = Actuator Override Switch   (usage 0xA6)
//   bit4 = Actuator Power             (usage 0x94)
//   bits5-7 = padding constante (0)
inline uint8_t buildPidStateByte(bool devicePaused, bool actuatorsEnabled,
                                 bool safetySwitch, bool actuatorOverride,
                                 bool actuatorPower)
{
    return (uint8_t)((devicePaused ? 1 : 0) |
                     (actuatorsEnabled ? 2 : 0) |
                     (safetySwitch ? 4 : 0) |
                     (actuatorOverride ? 8 : 0) |
                     (actuatorPower ? 16 : 0));
}
