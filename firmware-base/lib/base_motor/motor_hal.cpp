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

namespace drivelab {

// ----------------------------------------------------------------------------
// FocEncoder
// ----------------------------------------------------------------------------
FocEncoder::FocEncoder(Encoder& encoder, float lpfHz, float expectedSampleRateHz)
    : m_encoder(encoder)
{
    m_velEst.lpf = makeLowPass(lpfHz, expectedSampleRateHz, 0.707f);
}

float FocEncoder::positionRad() { return m_encoder.getAngle(); }

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
    const long mv = busMilliVolts(static_cast<uint16_t>(analogRead(kOdrivePinVBus)), 3300);
    return static_cast<float>(mv) / 1000.0f;
}

float FocPower::mosfetTempC()
{
    // AJUSTAR: as constantes do NTC dos FETs em sensor_convert.h
    // (kNtcR25/kNtcBeta/kNtcRload) foram conferidas contra o firmware ODrive
    // GENUÍNO; a MKS ODRIVE-S (clone) pode divergir em valores de pull-up/
    // NTC — risco documentado, não bloqueante para este passo (a leitura de
    // kOdrivePinNtcM0 nem entra em uso real até a Task 4). Placeholder
    // seguro até a calibração de bancada.
    return 25.0f;
}

float FocPower::motorTempC()
{
    // AJUSTAR: sem NTC dedicado no motor confirmado nesta revisão de placa —
    // placeholder seguro (mesma nota de mosfetTempC()).
    return 25.0f;
}

// ----------------------------------------------------------------------------
// FocMotor
// ----------------------------------------------------------------------------
void FocMotor::setTorque(float nm) { m_motor.move(nm); }
void FocMotor::disable() { m_motor.disable(); }

// ----------------------------------------------------------------------------
// FocBrake
// ----------------------------------------------------------------------------
void FocBrake::setDuty(float duty01)
{
    if (kOdrivePinBrakeRes < 0)
        return; // AJUSTAR — pino não confirmado, não aciona nada

    const int duty = static_cast<int>(duty01 * 255.0f);
    analogWrite(kOdrivePinBrakeRes, duty);
}

}  // namespace drivelab
