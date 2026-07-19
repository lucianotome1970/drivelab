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

// Lê os sensores por ADC e atualiza o cache. Chamar periodicamente (~10 Hz) no
// loop() — não no envio de telemetria (que é mais frequente).
void sensorsSample();

// Temp do die do MCU (°C) — sensor interno do F405, validado na bancada.
int8_t sensorMcuTempC();

// NOTA: temp dos FETs e tensão do barramento foram adiadas para o M1. Os NTC
// onboard e o divisor do barramento da MKS ODRIVE-S (clone) divergem do ODrive
// genuíno (na bancada o FET saturava em 127°C e o bus lia ~2.2V flutuante sem
// DC). As conversões puras (fetThermistorCentiC/busMilliVolts) já estão prontas
// e testadas em sensor_convert.h; faltam os pinos/escala reais do clone, que
// serão medidos no M1 (com motor + DC ligados, quando viram úteis).
