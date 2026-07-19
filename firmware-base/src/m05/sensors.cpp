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

// Cache atualizado por sensorsSample(); lido pelos getters no envio da telemetria.
static int8_t   s_mcuTempC = 0;
static int8_t   s_fetTempC = 0;
static uint16_t s_busMv    = 0;
static bool     s_adcInit  = false;

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

    // VDDA real (via VREFINT) — usado para escalar temp do MCU e VBUS.
    int vdda = vddaMilliVolts(static_cast<uint16_t>(analogRead(AVREF)));

    // Temp do MCU (sensor interno).
    int vsenseMv = adcCountsToMilliVolts(static_cast<uint16_t>(analogRead(ATEMP)), vdda);
    s_mcuTempC = clampToSbyte(mcuTempCFromSenseMv(vsenseMv));

    // Tensão do barramento DC (lê ~0 sem a fonte ligada).
    long busMv = busMilliVolts(static_cast<uint16_t>(analogRead(PA6)), vdda);
    if (busMv < 0)      busMv = 0;
    if (busMv > 65535)  busMv = 65535;
    s_busMv = static_cast<uint16_t>(busMv);

    // Temp dos FETs: máx(M0,M1) — pior caso p/ over-temp. Em centésimos, depois °C.
    int m0Centi = fetThermistorCentiC(static_cast<uint16_t>(analogRead(PC5)));
    int m1Centi = fetThermistorCentiC(static_cast<uint16_t>(analogRead(PA4)));
    int maxCenti = (m0Centi > m1Centi) ? m0Centi : m1Centi;
    s_fetTempC = clampToSbyte(maxCenti / 100);
}

int8_t   sensorMcuTempC()      { return s_mcuTempC; }
int8_t   sensorFetTempC()      { return s_fetTempC; }
uint16_t sensorBusMilliVolts() { return s_busMv; }
