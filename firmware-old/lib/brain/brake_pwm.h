// ============================================================================
//  DriveLab Firmware
//  brake_pwm.h — Mapeamento puro duty→timings do brake chopper (TIM2 do ODrive v3.6 / MKS ODRIVE-S).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// A PARTE PORTÁVEL e testável do brake chopper: converter um duty (0..1) nos dois compares do TIM2
// (CH3 = low-side, CH4 = high-side) com dead-time inserido por SOFTWARE — porque o TIM2 é um timer
// general-purpose (sem BDTR/dead-time por hardware, ao contrário do TIM1 das fases do motor).
//
// Fonte autoritativa (portado 1:1): fork ODrive v0.5.1 da MKS —
//   Board/v3/Src/tim.c ............ config do TIM2 (center-aligned, period, PWM2, polaridades)
//   MotorControl/low_level.cpp .... update_brake_current() (duty→high_on/low_off) e
//                                   safety_critical_apply_brake_resistor_timings() (checagem de dead-time)
//
// O acionamento REAL do timer (registradores/HAL) mora em motor_hal.cpp (ARDUINO), atrás de flag e
// DESARMADO por padrão. Este header é puro/host-testável (nenhum registrador).
#pragma once

#include "ffb_math.h"   // clampf
#include <cstdint>

namespace drivelab {

// Constantes do TIM2 do brake (valores idênticos ao fork MKS: TIM_APB1_* em Board/v3/Inc/main.h).
static constexpr uint32_t kBrakePeriodClocks   = 4096;   ///< ARR (TIM_APB1_PERIOD_CLOCKS); center-align → ~10.25kHz @84MHz
static constexpr uint32_t kBrakeDeadtimeClocks = 40;     ///< dead-time por SW (~476ns @84MHz) entre low/high
static constexpr float    kBrakeMaxDuty        = 0.95f;  ///< teto do duty (deixa os bootstrap caps carregarem) — idem ODrive

/// Timings do TIM2: CCR3 (low-side) e CCR4 (high-side).
struct BrakeTimings {
    uint32_t lowOff;   ///< CCR3 — low side
    uint32_t highOn;   ///< CCR4 — high side
};

/// Estado DESARMADO/seguro (resistor não conduz): CCR3=0, CCR4=period+1 (compare nunca dispara).
inline BrakeTimings brakeSafeTimings() { return { 0u, kBrakePeriodClocks + 1u }; }

/// duty (0..1) → timings, exatamente como o ODrive (update_brake_current):
///   high_on = period*(1-duty);  low_off = high_on - dead-time (>=0);  duty saturado em [0, 0.95].
/// O gap (high_on - low_off) = dead-time é o que evita shoot-through (os dois FETs conduzindo juntos).
inline BrakeTimings brakeDutyToTimings(float duty01) {
    const float duty = clampf(duty01, 0.0f, kBrakeMaxDuty);
    const uint32_t highOn = static_cast<uint32_t>(static_cast<float>(kBrakePeriodClocks) * (1.0f - duty));
    const uint32_t lowOff = highOn >= kBrakeDeadtimeClocks ? (highOn - kBrakeDeadtimeClocks) : 0u;
    return { lowOff, highOn };
}

/// Checagem de segurança (mesma do safety_critical_apply_brake_resistor_timings): o gap entre high e low
/// TEM que ser >= dead-time; senão é violação (risco de shoot-through) e o acionamento deve recusar/faultar.
inline bool brakeDeadtimeOk(const BrakeTimings& t) {
    return (t.highOn >= t.lowOff) && ((t.highOn - t.lowOff) >= kBrakeDeadtimeClocks);
}

}  // namespace drivelab
