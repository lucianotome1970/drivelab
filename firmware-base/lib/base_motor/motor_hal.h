// ============================================================================
//  DriveLab Firmware
//  motor_hal.h — Adapters que "costuram" o cérebro FFB (lib/brain/hal.h) ao
//  SimpleFOC + ADCs da placa (ODrive v3.6 / MKS ODRIVE-S V3.6-S6V). M5,
//  Task 3: só CONSTRÓI a costura (nenhuma chamada aqui gera PWM ou habilita
//  o motor) — quem chama IMotor::setTorque()/loopFOC()/engine.step() é
//  trabalho de uma task futura (Task 4). Só a DECLARAÇÃO das classes mora
//  aqui (mesmo padrão de drv8301.h/.cpp); os corpos dos métodos (que usam
//  Arduino.h/SimpleFOC.h) ficam em motor_hal.cpp.
//
//  FocEncoder::velocityRadPerSec() é o ponto central deste passo: hoje o
//  cérebro só tem acesso à velocidade CRUA do SimpleFOC (encoder.
//  getVelocity()); aqui ela passa pelo estágio low-pass da VelocityEstimator
//  (lib/brain/filters.h) antes de chegar no cérebro — "velocidade
//  filtrada". Ver o comentário no .cpp para por que usamos só o `lpf`
//  público da VelocityEstimator (não `update()`).
//
//  Guardado por `#ifdef ARDUINO` (mesmo padrão de drv8301.h) — depende de
//  SimpleFOC.h/Arduino.h, então não compila (nem precisa) no host; os testes
//  de host cobrem a VelocityEstimator isoladamente (filters.h é puro), ver
//  test/test_velocity_estimator.cpp.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#ifdef ARDUINO

#include "hal.h"
#include "filters.h"
#include "drv8301.h"

class Encoder;    // forward decl — evita puxar SimpleFOC.h neste header
class BLDCMotor;  //   (SimpleFOC.h já é incluído por quem instancia estas
                   //   classes, ex.: src/m5/main.cpp)

namespace drivelab {

// ----------------------------------------------------------------------------
// FocEncoder — IEncoder sobre o `Encoder` do SimpleFOC, com velocidade
// filtrada (P1-3 / Task 3).
// ----------------------------------------------------------------------------
class FocEncoder : public IEncoder
{
public:
    // encoder: referência ao `Encoder` já construído em main.cpp (não é dono).
    // lpfHz/expectedSampleRateHz: corte do low-pass e taxa de chamada ESPERADA
    // de velocityRadPerSec() (para desenhar o biquad) — AJUSTAR na bancada
    // quando a Task 4 decidir a taxa real do loop do engine (o corte do
    // filtro efetivamente muda se a taxa de chamada real divergir muito da
    // taxa assumida aqui).
    explicit FocEncoder(Encoder& encoder, float lpfHz = 30.0f, float expectedSampleRateHz = 1000.0f);

    float positionRad() override;
    float velocityRadPerSec() override;

    void reset() { m_velEst.reset(); }

private:
    Encoder& m_encoder;
    VelocityEstimator m_velEst;
};

// ----------------------------------------------------------------------------
// FocCurrent — ICurrentSense sobre os shunts B/C do ODrive v3.6 (fase A
// reconstruída por KCL, mesmo esquema do firmware oficial). Usado só pela
// proteção de sobrecorrente (v1 é voltage-torque, não current-control).
// ----------------------------------------------------------------------------
class FocCurrent : public ICurrentSense
{
public:
    explicit FocCurrent(Drv8301Gain gain);

    void readPhaseCurrents(float& ia, float& ib, float& ic) override;

private:
    // AJUSTAR na bancada: offset de meio-fundo de escala (2048 counts ~ 0A)
    // é um placeholder — o offset real do amp de corrente do DRV8301 tem
    // tolerância própria e deve ser calibrado com o motor parado/sem
    // corrente antes de confiar neste valor para qualquer proteção.
    static constexpr uint16_t kOffsetCounts = 2048;
    static constexpr float kShuntOhm = 0.0005f; // 500 µΩ (ODrive v3.6, HW_VERSION_MINOR > 3)

    // AJUSTAR: VDDA nominal (3.3V) — não medimos o VREFINT interno aqui (v1
    // simplificado; sensor_convert.h já tem vddaMilliVolts(VREFINT) pronto
    // para refinar isso numa task futura se a precisão nominal não bastar).
    static constexpr float kVddaNominalV = 3.3f;

    static float gainToVPerV(Drv8301Gain gain);
    float countsToAmps(int counts) const;

    float m_gainVPerV;
};

// ----------------------------------------------------------------------------
// FocPower — IPowerSense: tensão de barramento via busMilliVolts() (sensor_
// convert.h); temperaturas com placeholder seguro (AJUSTAR — ver .cpp).
// ----------------------------------------------------------------------------
class FocPower : public IPowerSense
{
public:
    float busVoltage() override;
    float mosfetTempC() override;
    float motorTempC() override;
};

// ----------------------------------------------------------------------------
// FocMotor — IMotor sobre o `BLDCMotor` do SimpleFOC. setTorque()/disable()
// EXISTEM aqui mas não são chamados por ninguém nesta task (nem main.cpp,
// nem engine.step — isso é Task 4). O motor continua parado.
// ----------------------------------------------------------------------------
class FocMotor : public IMotor
{
public:
    explicit FocMotor(BLDCMotor& motor) : m_motor(motor) {}

    void setTorque(float nm) override;
    void disable() override;

private:
    BLDCMotor& m_motor;
};

// ----------------------------------------------------------------------------
// FocBrake — IBrakeResistor sobre o meio-ponte (half-bridge) do resistor de
// frenagem. Os pinos físicos (kOdrivePinAuxBrakeL/H, odrive_v36_pins.h) FORAM
// confirmados na M5 Task 4 (fonte de fábrica MKS v0.5.1), mas a PWM própria
// desse half-bridge (canais/dead-time do TIM2 dedicados a AUX_L/AUX_H) ainda
// não foi portada — é diferente de um único MOSFET low-side controlável via
// analogWrite(), que era a suposição antiga. setDuty() continua NO-OP no v1
// de propósito (ver comentário completo em motor_hal.cpp); esta task não
// chama setDuty() de lugar nenhum de qualquer forma.
// ----------------------------------------------------------------------------
class FocBrake : public IBrakeResistor
{
public:
    void setDuty(float duty01) override;
};

}  // namespace drivelab

#endif  // ARDUINO
