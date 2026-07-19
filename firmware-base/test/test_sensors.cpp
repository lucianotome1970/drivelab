// ============================================================================
//  DriveLab Firmware
//  test_sensors.cpp — Testes de HOST das conversões puras de sensores
//  (sensor_convert.h): VDDA via VREFINT, counts->mV, bus voltage, temp do MCU
//  (fórmula datasheet) e temp do NTC dos FETs (fórmula Beta). Roda sem placa.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Roda sem placa nenhuma: firmware-base/test/run.sh

#include "../lib/base_shared/sensor_convert.h"

#include <cstdio>

static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { \
        ++g_checks; \
        if (!(cond)) { ++g_fails; std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } \
    } while (0)

int main()
{
    // ----- vddaMilliVolts -----
    {
        CHECK(vddaMilliVolts(0) == 3300);            // fallback defensivo
        // VREFINT=1.21V com VDDA=3.3V -> counts = 1210*4096/3300 ≈ 1502
        int vdda = vddaMilliVolts(1502);
        CHECK(vdda >= 3290 && vdda <= 3310);         // ~3300 mV
        // VDDA menor -> counts do VREFINT maior
        CHECK(vddaMilliVolts(1600) < vddaMilliVolts(1502));
    }

    // ----- adcCountsToMilliVolts -----
    {
        CHECK(adcCountsToMilliVolts(0, 3300) == 0);
        CHECK(adcCountsToMilliVolts(2048, 3300) == 1650);   // meia escala
        CHECK(adcCountsToMilliVolts(4096, 3300) == 3300);
    }

    // ----- busMilliVolts (ratio 19) -----
    {
        CHECK(busMilliVolts(0, 3300) == 0);
        long full = busMilliVolts(4095, 3300);              // ~62.7 V
        CHECK(full > 62000 && full < 63000);
        // linearidade aproximada: metade dos counts ~ metade da tensão
        CHECK(busMilliVolts(2048, 3300) > 31000 && busMilliVolts(2048, 3300) < 31600);
    }

    // ----- mcuTempCFromSenseMv (V25=760, slope 2.5mV/°C) -----
    {
        CHECK(mcuTempCFromSenseMv(760) == 25);
        CHECK(mcuTempCFromSenseMv(785) == 35);              // +25mV -> +10°C
        CHECK(mcuTempCFromSenseMv(735) == 15);              // -25mV -> -10°C
    }

    // ----- fetThermistorCentiC (Beta 3434, R25 10k, Rload 3k3, NTC embaixo) -----
    {
        // counts fora de faixa = inválido
        CHECK(fetThermistorCentiC(0) == -12800);
        CHECK(fetThermistorCentiC(4095) == -12800);

        // R_ntc = 10k em counts=3080 -> ~25°C
        int c25 = fetThermistorCentiC(3080);
        CHECK(c25 > 2400 && c25 < 2600);

        // NTC: mais quente => R menor => counts menor. Monotônico decrescente.
        int hot = fetThermistorCentiC(2048);   // ~57°C
        int cold = fetThermistorCentiC(3600);  // ~4°C
        CHECK(hot > c25);
        CHECK(cold < c25);
        CHECK(hot > 5000 && hot < 6500);        // ~57°C
        CHECK(cold > 0 && cold < 1200);         // ~4°C
    }

    std::printf("sensors: %d checks, %d fails\n", g_checks, g_fails);
    return g_fails == 0 ? 0 : 1;
}
