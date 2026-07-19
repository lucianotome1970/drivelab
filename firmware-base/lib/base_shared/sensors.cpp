// ============================================================================
//  DriveLab Firmware
//  sensors.cpp — Implementação dos reads de ADC dos sensores da base
//  (STM32F405 / ODrive v3.6). Pinos confirmados no firmware oficial do ODrive:
//    VBUS  = PA6  (ADC1 ch6, divisor do barramento)
//    FET M0 = PC5 (ADC1 ch15, NTC onboard)
//    FET M1 = PA4 (ADC1 ch4,  NTC onboard)
//    MCU temp = ATEMP (ch16) ; VREFINT = AVREF (ch17), p/ VDDA
//  As conversões vivem em sensor_convert.h (testadas no host).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include "sensors.h"

#include <Arduino.h>

#include "sensor_convert.h"

// Cache atualizado por sensorsSample(); lido pelo getter no envio da telemetria.
static int8_t s_mcuTempC = 0;
static bool   s_adcInit  = false;

static int8_t clampToSbyte(int celsius)
{
    if (celsius < -128) return -128;
    if (celsius > 127)  return 127;
    return static_cast<int8_t>(celsius);
}

void sensorsSample()
{
    // O core do STM32duino usa 10 bits por padrão; nossas conversões assumem
    // escala cheia de 12 bits (0..4095). Setar uma vez.
    if (!s_adcInit)
    {
        analogReadResolution(12);
        s_adcInit = true;
    }

    // VDDA real (via VREFINT) — usado para escalar a tensão do sensor de temp.
    int vdda = vddaMilliVolts(static_cast<uint16_t>(analogRead(AVREF)));

    // Temp do MCU (sensor interno do F405). Único sensor confiável no M0.5.
    int vsenseMv = adcCountsToMilliVolts(static_cast<uint16_t>(analogRead(ATEMP)), vdda);
    s_mcuTempC = clampToSbyte(mcuTempCFromSenseMv(vsenseMv));

    // FET temp e bus voltage adiados p/ o M1 (ver nota em sensors.h): os pinos
    // do clone MKS divergem do ODrive genuíno. As conversões puras já existem
    // em sensor_convert.h; falta medir os pinos/escala reais com DC+motor.
}

int8_t sensorMcuTempC() { return s_mcuTempC; }
