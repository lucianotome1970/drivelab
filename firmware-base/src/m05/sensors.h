// ============================================================================
//  DriveLab Firmware
//  sensors.h — Leitura dos sensores da base por ADC (temp do MCU, temp dos
//  FETs, tensão do barramento) para a telemetria 0x21. A matemática pura fica
//  em sensor_convert.h (testada no host); aqui só o acesso ao hardware
//  (analogRead) + cache. Read-only, sem tocar motor/potência (seguro no M0.5).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cstdint>

// Lê todos os sensores por ADC e atualiza o cache. Chamar periodicamente
// (~10 Hz) no loop() — não no envio de telemetria (que é mais frequente).
void sensorsSample();

// Valores do cache (clampados aos tipos do wire format da telemetria 0x21).
int8_t   sensorMcuTempC();        // temp do die do MCU (°C)
int8_t   sensorFetTempC();        // máx(M0,M1) dos NTC dos FETs (°C)
uint16_t sensorBusMilliVolts();   // tensão do barramento DC (mV) — ~0 sem DC
