// ============================================================================
//  DriveLab Firmware
//  hal.h — Interfaces de HARDWARE (encoder, sense de corrente, motor) p/ mockar em teste.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// A "costura" (seam) de hardware do cérebro FFB. No firmware real, cada interface é
// implementada sobre SimpleFOC / ADC / encoder; nos testes de host, por mocks com valores
// em variáveis (o "HIL simplificado"). O ffb_controller.h só conhece estas interfaces —
// nunca o hardware — então a lógica é testável no PC sem placa.
#pragma once

namespace drivelab {

/// Posição/velocidade do eixo (no firmware: encoder ABZ/SPI via SimpleFOC).
struct IEncoder {
    virtual ~IEncoder() = default;
    virtual float positionRad() = 0;
    virtual float velocityRadPerSec() = 0;
};

/// Corrente das fases (no firmware: shunts + ADC). Usado só para a proteção por sobrecorrente.
struct ICurrentSense {
    virtual ~ICurrentSense() = default;
    virtual void readPhaseCurrents(float& ia, float& ib, float& ic) = 0;
};

/// Atuador (no firmware: BLDCMotor do SimpleFOC em modo torque).
struct IMotor {
    virtual ~IMotor() = default;
    virtual void setTorque(float nm) = 0;
    virtual void disable() = 0;
};

/// Tensão do barramento + temperaturas (no firmware: ADC + NTC). Espelha a telemetria do app
/// (bus voltage + FET/motor). Usado no M2 pela proteção e pelo brake resistor.
struct IPowerSense {
    virtual ~IPowerSense() = default;
    virtual float busVoltage() = 0;   ///< V
    virtual float mosfetTempC() = 0;
    virtual float motorTempC() = 0;
};

/// Brake resistor — dissipa a energia de regeneração (no firmware: PWM num MOSFET dedicado).
struct IBrakeResistor {
    virtual ~IBrakeResistor() = default;
    virtual void setDuty(float duty01) = 0;   ///< 0..1
};

}  // namespace drivelab
