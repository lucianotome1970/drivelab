// ============================================================================
//  DriveLab Firmware
//  motor_hal.cpp — Corpos dos métodos dos adapters declarados em motor_hal.h
//  (SimpleFOC/Arduino de verdade). Nenhuma função aqui gera PWM ou habilita
//  o motor — ver o cabeçalho de motor_hal.h para o escopo exato da Task 3.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include <Arduino.h>
#include <SimpleFOC.h>

#include "motor_hal.h"
#include "odrive_v36_pins.h"
#include "sensor_convert.h"
#include "brake_pwm.h"

namespace drivelab {

// ----------------------------------------------------------------------------
// FocEncoder
// ----------------------------------------------------------------------------
FocEncoder::FocEncoder(Encoder& encoder, float lpfHz, float expectedSampleRateHz)
    : m_encoder(encoder)
{
    m_velEst.lpf = makeLowPass(lpfHz, expectedSampleRateHz, 0.707f);
}

float FocEncoder::positionRad() { return m_encoder.getAngle() - m_centerOffsetRad; }

void FocEncoder::setCenterHere() { m_centerOffsetRad = m_encoder.getAngle(); }

float FocEncoder::velocityRadPerSec()
{
    // IMPORTANTE: chamamos `m_velEst.lpf.process(...)` — só o estágio Biquad
    // PÚBLICO da VelocityEstimator — e NÃO `m_velEst.update(vel, dt)`.
    // `update()` faz diferença finita de POSIÇÃO (não de velocidade) seguida
    // do low-pass; alimentá-lo com uma velocidade já diferenciada pelo
    // SimpleFOC (encoder.getVelocity()) calcularia aceleração/jerk filtrado,
    // não velocidade suave — o oposto do que este passo pede ("velocidade
    // filtrada"). Reaproveitamos o mesmo low-pass (RBJ, testado em
    // test/test_velocity_estimator.cpp) só que aplicado direto sobre a
    // estimativa de velocidade que o próprio SimpleFOC já calcula.
    return m_velEst.lpf.process(m_encoder.getVelocity());
}

// ----------------------------------------------------------------------------
// FocCurrent
// ----------------------------------------------------------------------------
FocCurrent::FocCurrent(Drv8301Gain gain) : m_gainVPerV(gainToVPerV(gain))
{
    analogReadResolution(12); // uma vez — ADC de 12 bits (0..4095), ver sensor_convert.h
}

void FocCurrent::readPhaseCurrents(float& ia, float& ib, float& ic)
{
    const int rawB = analogRead(kOdrivePinShuntIB);
    const int rawC = analogRead(kOdrivePinShuntIC);

    ib = countsToAmps(rawB);
    ic = countsToAmps(rawC);
    ia = -(ib + ic); // KCL — soma das 3 fases = 0 (fase A sem shunt dedicado)
}

float FocCurrent::gainToVPerV(Drv8301Gain gain)
{
    switch (gain)
    {
        case Drv8301Gain::G10: return 10.0f;
        case Drv8301Gain::G20: return 20.0f;
        case Drv8301Gain::G40: return 40.0f;
        case Drv8301Gain::G80: return 80.0f;
    }
    return 20.0f; // não deveria chegar aqui — default conservador
}

float FocCurrent::countsToAmps(int counts) const
{
    // amps = (counts - offset) * (VDDA / 4096) / (ganho_V/V * shunt_ohm)
    //
    // CONFIRMADO (M5 Task 4) contra o firmware oficial ODrive
    // (phase_current_from_adcval): com o gain de fábrica G40 (40 V/V,
    // gainToVPerV() abaixo) e o shunt de 500µΩ desta placa (kShuntOhm),
    // esta fórmula reduz a amps = (counts-2048) * 0.0403 A/count —
    //   (3.3/4096) * (1/40) * (1/500e-6) ≈ 0.0403.
    // O offset (kOffsetCounts=2048, meio da escala do ADC de 12 bits) é só
    // um placeholder até a calibração de bancada com o motor parado/sem
    // corrente (ver comentário em motor_hal.h).
    const float voltsAtAmp = (static_cast<float>(counts) - static_cast<float>(kOffsetCounts))
                            * (kVddaNominalV / static_cast<float>(kAdcFullScale));
    return voltsAtAmp / (m_gainVPerV * kShuntOhm);
}

// ----------------------------------------------------------------------------
// FocPower
// ----------------------------------------------------------------------------
float FocPower::busVoltage()
{
    // Mesmo VDDA nominal do FocCurrent (v1 simplificado — sem medir VREFINT).
    const long mv = busMilliVolts(static_cast<uint16_t>(analogRead(kOdrivePinVBus)), 3300, m_dividerRatio);
    return static_cast<float>(mv) / 1000.0f;
}

float FocPower::mosfetTempC()
{
    // NTC onboard dos FETs (PC5 / kOdrivePinNtcM0), convertido pela fórmula Beta (fetThermistorCentiC).
    // Leitura PASSIVA (motor OFF ok). AJUSTAR na bancada: as constantes kNtcR25/kNtcBeta/kNtcRload foram
    // conferidas contra o ODrive GENUÍNO; a MKS (clone) pode divergir no pull-up/NTC — validar contra um
    // termômetro. Sensor aberto/curto → fetThermistorCentiC devolve -128.00°C (o app mostra "sem sensor").
    const int centiC = fetThermistorCentiC(static_cast<uint16_t>(analogRead(kOdrivePinNtcM0)));
    return static_cast<float>(centiC) / 100.0f;
}

float FocPower::motorTempC()
{
#ifdef DRVLAB_MOTOR_NTC
    // NTC no ENROLAMENTO do motor, lido no AUX_TEMP (PA5). Mesmo NTC/fórmula do FET (MF52A 10k, Beta 3435 ≈
    // kNtcBeta=3434) → reaproveita fetThermistorCentiC. AJUSTAR na bancada: confirmar o pull-up do AUX_TEMP
    // (assume 3k3, igual ao M0_TEMP) contra um termômetro. Sensor aberto/curto → -128°C ("sem sensor").
    // O corte por sobretemperatura deve ser conservador (~105°C): o bead de epóxi (MF52A) só vai a ~125°C.
    const int centiC = fetThermistorCentiC(static_cast<uint16_t>(analogRead(kOdrivePinAuxTemp)));
    return static_cast<float>(centiC) / 100.0f;
#else
    // Sem NTC de motor instalado: "sem sensor" (o app mostra "—" e a segurança usa max(FET,motor) sem falso
    // aquecimento). Defina DRVLAB_MOTOR_NTC ao soldar o NTC no AUX_TEMP (PA5).
    return -128.0f;
#endif
}

// ----------------------------------------------------------------------------
// FocMotor
// ----------------------------------------------------------------------------
void FocMotor::setTorque(float nm) { m_motor.move(nm); }
void FocMotor::disable() { m_motor.disable(); }

// ----------------------------------------------------------------------------
// FocBrake — chopper do brake resistor (meio-ponte AUX_L=PB10/AUX_H=PB11, TIM2).
//
// Portado 1:1 do fork MKS v0.5.1 (Board/v3/Src/tim.c + MotorControl/low_level.cpp): TIM2 center-aligned,
// CH3=low-side (PWM2/pol LOW), CH4=high-side (PWM2/pol HIGH), dead-time por SOFTWARE (brake_pwm.h). A
// matemática duty→timings é pura/testada em brake_pwm.h; aqui só o acionamento dos registradores.
//
// SEGURANÇA DUPLA:
//  1) tudo atrás de DRVLAB_BRAKE_CHOPPER_HW — no build PADRÃO nada disto compila e o TIM2 fica INTOCADO
//     (comportamento idêntico ao NO-OP antigo). Habilitar é passo de bancada (definir a flag + escopo).
//  2) DESARMADO por padrão (m_armed=false) — mesmo com HW e begin(), setDuty() só mantém o estado seguro
//     até arm() ser chamado DELIBERADAMENTE (com os gates verificados no osciloscópio).
// ----------------------------------------------------------------------------
#ifdef DRVLAB_BRAKE_CHOPPER_HW
static TIM_HandleTypeDef s_htim2;
static bool s_tim2Ready = false;

// Estado seguro nos compares: CCR3=0, CCR4=period+1 → resistor NÃO conduz (idem safety_critical_*_brake).
static inline void brakeSafeRegisters()
{
    TIM2->CCR3 = 0;
    TIM2->CCR4 = kBrakePeriodClocks + 1;
}
#endif

void FocBrake::begin()
{
#ifdef DRVLAB_BRAKE_CHOPPER_HW
    __HAL_RCC_TIM2_CLK_ENABLE();
    __HAL_RCC_GPIOB_CLK_ENABLE();

    GPIO_InitTypeDef g = {};
    g.Pin       = GPIO_PIN_10 | GPIO_PIN_11;   // PB10=TIM2_CH3 (low), PB11=TIM2_CH4 (high)
    g.Mode      = GPIO_MODE_AF_PP;
    g.Pull      = GPIO_NOPULL;
    g.Speed     = GPIO_SPEED_FREQ_LOW;
    g.Alternate = GPIO_AF1_TIM2;
    HAL_GPIO_Init(GPIOB, &g);

    s_htim2.Instance               = TIM2;
    s_htim2.Init.Prescaler         = 0;
    s_htim2.Init.CounterMode       = TIM_COUNTERMODE_CENTERALIGNED3;
    s_htim2.Init.Period            = kBrakePeriodClocks;
    s_htim2.Init.ClockDivision     = TIM_CLOCKDIVISION_DIV1;
    s_htim2.Init.AutoReloadPreload = TIM_AUTORELOAD_PRELOAD_DISABLE;
    HAL_TIM_PWM_Init(&s_htim2);

    TIM_OC_InitTypeDef oc = {};
    oc.OCMode     = TIM_OCMODE_PWM2;
    oc.OCFastMode = TIM_OCFAST_DISABLE;

    oc.Pulse       = 0;                          // CH3 low-side: pol LOW, estado seguro
    oc.OCPolarity  = TIM_OCPOLARITY_LOW;
    HAL_TIM_PWM_ConfigChannel(&s_htim2, &oc, TIM_CHANNEL_3);

    oc.Pulse       = kBrakePeriodClocks + 1;     // CH4 high-side: pol HIGH, estado seguro
    oc.OCPolarity  = TIM_OCPOLARITY_HIGH;
    HAL_TIM_PWM_ConfigChannel(&s_htim2, &oc, TIM_CHANNEL_4);

    brakeSafeRegisters();                         // garante o estado seguro ANTES de habilitar as saídas
    HAL_TIM_PWM_Start(&s_htim2, TIM_CHANNEL_3);
    HAL_TIM_PWM_Start(&s_htim2, TIM_CHANNEL_4);
    s_tim2Ready = true;
#endif
    m_armed = false;   // SEMPRE começa desarmado
}

void FocBrake::arm() { m_armed = true; }

void FocBrake::disarm()
{
    m_armed = false;
#ifdef DRVLAB_BRAKE_CHOPPER_HW
    if (s_tim2Ready) brakeSafeRegisters();
#endif
}

void FocBrake::setDuty(float duty01)
{
#ifdef DRVLAB_BRAKE_CHOPPER_HW
    if (!s_tim2Ready) return;
    if (!m_armed) { brakeSafeRegisters(); return; }        // desarmado → resistor não conduz

    const BrakeTimings t = brakeDutyToTimings(duty01);
    if (!brakeDeadtimeOk(t)) { brakeSafeRegisters(); return; } // recusa violação de dead-time (shoot-through)

    // Atualização segura (idem ODrive): volta ao estado seguro e então aplica os dois compares.
    brakeSafeRegisters();
    TIM2->CCR3 = t.lowOff;   // CH3 low-side
    TIM2->CCR4 = t.highOn;   // CH4 high-side
#else
    (void)duty01;   // build PADRÃO: NO-OP — TIM2 intocado (mesma segurança do v1)
#endif
}

}  // namespace drivelab
