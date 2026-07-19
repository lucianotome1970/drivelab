// ============================================================================
//  DriveLab Firmware
//  avr_compat.h — Ponte de compatibilidade AVR→STM32 p/ libs que assumem <util/delay.h>.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// A ArduinoJoystickWithFFBLibrary (DynamicHID.cpp) usa a macro AVR _delay_us(),
// que não existe no core STM32duino. Mapeamos para delayMicroseconds() (equivalente
// Arduino). Force-included via `-include avr_compat.h` no env m05 — assim não editamos
// a lib de terceiros (re-baixada pelo lib_deps). delayMicroseconds já está declarado
// em Arduino.h, incluído no ponto de uso.
#pragma once

#ifndef _delay_us
#define _delay_us(x) delayMicroseconds(x)
#endif
