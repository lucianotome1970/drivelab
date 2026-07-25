// ============================================================================
//  DriveLab Firmware
//  sensor_convert.h — Conversões PURAS de leituras de ADC para grandezas
//  físicas (sem Arduino/HAL, testáveis no host como base_cfg): tensão do
//  barramento, temperatura do NTC dos FETs (fórmula β), tensão VDDA a partir
//  do VREFINT e temperatura do sensor interno do MCU (constantes do datasheet
//  do F405). Constantes conferidas no firmware oficial do ODrive v3.6.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cmath>
#include <cstdint>

// ADC de 12 bits (0..4095), escala cheia 4096.
static constexpr int kAdcFullScale = 4096;

// Divisor do barramento DC do ODrive v3.6. O fork MKS (main.h) define VBUS_S_DIVIDER_RATIO por VARIANTE
// de tensão: 19 p/ placa 48/56V e 11 p/ placa 24V — é o ÚNICO parâmetro que muda entre 24V e 56V. Mantido
// como default (56V) p/ compat; o valor efetivo vem de vbusDividerRatioFor() derivado da nominal escolhida
// no app, pra UM binário atender 24V e 56V sem recompilar. Ainda assim, verificar na bancada com tensão
// conhecida (clone pode divergir) — ver item A1 do registro de validação.
static constexpr int kVbusDividerRatio = 19;

// Divisor efetivo pela VARIANTE DA PLACA (hardware, BID_BOARD_VARIANT): placa 24V → 11, placa 56V → 19.
// É propriedade FÍSICA da placa (resistores do divisor), INDEPENDENTE da tensão de operação/fonte — por
// isso vem de um setting próprio, não de busNominalV. Assim uma placa 56V rodando uma fonte 24V (usuário
// põe operação=24V) continua lendo a tensão certa. Um binário atende as duas placas sem recompilar.
inline int vbusDividerForVariant(uint8_t variant) { return variant ? 19 : 11; }  // 0=24V, 1=56V

// Checagem de PLAUSIBILIDADE da tensão do bus vs a nominal escolhida. Como não dá pra auto-detectar a
// variante (24V/56V) por hardware, isto pega o erro mais provável: o usuário escolheu a variante errada
// no app → o divisor fica errado → a tensão lida sai muito fora da nominal. É um AVISO (não um fault).
// Só julga com o bus ENERGIZADO (>= floor); com o bus desligado/USB retorna false (nada a julgar).
static constexpr float kBusPoweredFloorV = 8.0f;    // abaixo disso o barramento está desligado
static constexpr float kBusPlausLowFrac  = 0.70f;   // < 70% da nominal (energizado) = suspeito
static constexpr float kBusPlausHighFrac = 1.20f;   // > 120% da nominal = suspeito (acima do overvoltage 108%)

inline bool busVoltageImplausible(float measuredV, int nominalV)
{
    if (nominalV <= 0) return false;
    if (measuredV < kBusPoweredFloorV) return false;              // bus desligado → sem julgamento
    const float lo = static_cast<float>(nominalV) * kBusPlausLowFrac;
    const float hi = static_cast<float>(nominalV) * kBusPlausHighFrac;
    return measuredV < lo || measuredV > hi;
}

// NTC onboard dos FETs (ODrive v3.6): 10k a 25°C, Beta 3434, pull-up 3k3 a VDDA,
// NTC na perna de baixo -> divisor ratiométrico (v = counts/4096 = Vadc/VDDA).
static constexpr double kNtcR25   = 10000.0;
static constexpr double kNtcBeta  = 3434.0;
static constexpr double kNtcRload = 3300.0;
static constexpr double kNtcT0K   = 298.15; // 25°C em Kelvin

// VDDA (mV) a partir do VREFINT interno. F405: VREFINT típico 1.21V; o VREFINT_CAL
// do die pode estar em branco no F405, então usamos o típico. counts=0 -> fallback 3.3V.
inline int vddaMilliVolts(uint16_t vrefintCounts)
{
    if (vrefintCounts == 0)
        return 3300;
    return (1210 * kAdcFullScale) / vrefintCounts;
}

// Converte counts de ADC para mV, usando o VDDA medido (ratiométrico e mais
// preciso do que assumir 3.3V fixo).
inline int adcCountsToMilliVolts(uint16_t counts, int vddaMv)
{
    return static_cast<int>(static_cast<long>(counts) * vddaMv / kAdcFullScale);
}

// Tensão do barramento DC (mV) = tensão no pino * ratio do divisor. O ratio é parâmetro (default 56V=19),
// pra o mesmo binário atender 24V (11) e 56V (19) — quem passa o valor é o FocPower, derivado da nominal.
inline long busMilliVolts(uint16_t counts, int vddaMv, int dividerRatio = kVbusDividerRatio)
{
    return static_cast<long>(adcCountsToMilliVolts(counts, vddaMv)) * dividerRatio;
}

// Temperatura do sensor interno do MCU (°C) a partir da tensão do sensor (mV).
// F405 NÃO tem TS_CAL de fábrica -> constantes típicas do datasheet:
// V25 = 760 mV, slope = 2.5 mV/°C. T = (Vsense - 760)/2.5 + 25.
// (offset absoluto ruim; serve p/ tendência/over-temp, não termômetro calibrado.)
inline int mcuTempCFromSenseMv(int vsenseMv)
{
    return (vsenseMv - 760) * 2 / 5 + 25;
}

// Temperatura do NTC dos FETs em centésimos de °C, pela fórmula Beta.
// v = counts/4096 = R_ntc/(Rload+R_ntc) -> R_ntc = Rload*counts/(4096-counts).
// 1/T = 1/T0 + ln(R_ntc/R25)/Beta. Retorna °C*100 (o chamador clampa em sbyte).
// counts fora de faixa (0 / >=4096) = sensor aberto/curto -> valor claramente inválido.
inline int fetThermistorCentiC(uint16_t counts)
{
    if (counts == 0 || counts >= static_cast<uint16_t>(kAdcFullScale - 1))
        return -12800; // -128.00°C: sinaliza leitura inválida (fica no piso do sbyte)

    double rNtc = kNtcRload * static_cast<double>(counts) /
                  static_cast<double>(kAdcFullScale - counts);
    double invT = 1.0 / kNtcT0K + std::log(rNtc / kNtcR25) / kNtcBeta;
    double tC = 1.0 / invT - 273.15;
    return static_cast<int>(tC * 100.0 + (tC >= 0 ? 0.5 : -0.5));
}
