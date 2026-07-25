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

    // ----- divisor por variante (24V/56V) — um binário atende as duas sem recompilar -----
    {
        CHECK(vbusDividerRatioFor(24) == 11);   // placa 24V
        CHECK(vbusDividerRatioFor(48) == 19);   // placa 48/56V
        CHECK(vbusDividerRatioFor(56) == 19);
        CHECK(vbusDividerRatioFor(12) == 11);   // baixa nominal → variante 24V

        // O ratio passado muda a leitura: mesmos counts, tensão proporcional ao divisor.
        long r19 = busMilliVolts(2048, 3300, 19);
        long r11 = busMilliVolts(2048, 3300, 11);
        CHECK(r19 > r11);
        CHECK(busMilliVolts(2048, 3300) == r19);            // default = 19 (compat)
        // 24V: full-scale ~ counts*11 fica na casa dos 36V (coerente c/ placa 24V + headroom)
        long full24 = busMilliVolts(4095, 3300, 11);
        CHECK(full24 > 35000 && full24 < 37000);
    }

    // ----- plausibilidade da tensão vs nominal (aviso de variante 24V/56V errada) -----
    {
        // bus desligado (< floor 8V) → nunca julga
        CHECK(!busVoltageImplausible(0.0f, 56));
        CHECK(!busVoltageImplausible(5.0f, 24));

        // dentro da faixa [0.70, 1.20]·nominal → OK (inclui sag e regen normais)
        CHECK(!busVoltageImplausible(56.0f, 56));    // exato
        CHECK(!busVoltageImplausible(48.0f, 56));    // sag ~86%
        CHECK(!busVoltageImplausible(60.0f, 56));    // regen ~107%
        CHECK(!busVoltageImplausible(24.0f, 24));    // 24V exato

        // fora da faixa (energizado) → implausível (provável variante errada)
        CHECK(busVoltageImplausible(32.0f, 24));     // 133% — leu alto p/ 24V (placa é 56V?)
        CHECK(busVoltageImplausible(30.0f, 56));     // 54% da nominal 56 → suspeito (placa é 24V?)
        CHECK(busVoltageImplausible(15.0f, 24));     // 62% de 24 → suspeito
        // borda: 41/56 = 0.73 está DENTRO de [0.70,1.20] → plausível (não avisa)
        CHECK(!busVoltageImplausible(41.0f, 56));

        // nominal inválida → não julga
        CHECK(!busVoltageImplausible(50.0f, 0));
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
