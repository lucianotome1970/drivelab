// ============================================================================
//  DriveLab Firmware
//  main.cpp (m1) — ESQUELETO M1: motor open-loop com SimpleFOC + o cérebro FFB (lib/brain) via HAL.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Este arquivo LIGA o "cérebro" testável (lib/brain: FfbEngine + partida segura + proteção) ao
// SimpleFOC (BLDCMotor/driver/encoder) e ao ADC. É o alvo REAL das interfaces que mockamos no host.
//
// >>> ESQUELETO, NÃO validado em hardware. <<<
//   * Os PINOS e as ESCALAS de ADC são placeholders da ODESC v4.2 — AJUSTAR NA BANCADA.
//   * Compila para provar que o cérebro + SimpleFOC integram; o motor NÃO gira sozinho:
//     começa DESLIGADO e só arma via serial ('1' arma, '0' desarma) — segurança primeiro.
//   * Sequência: proteção (brake/over-V/temp) → partida (inter-travamentos + alinhamento + rampa)
//     → torque. No M1 não há jogo, então a força é 0 (o engine só faz a partida segura + proteção);
//     o canal USB FFB e o setGameForce() entram no M5.

#include <Arduino.h>
#include <SimpleFOC.h>
#include "ffb_engine.h"

using namespace drivelab;

// ===================== Pinos / parâmetros — AJUSTAR NA BANCADA (ODESC v4.2) =====================
static const int   PIN_PH_A = PA8,  PIN_PH_B = PA9,  PIN_PH_C = PA10;  // PWM das 3 fases
static const int   PIN_EN    = PB12;                                   // enable do driver
static const int   PIN_ENC_A = PB4,  PIN_ENC_B = PB5;                  // encoder incremental A/B
static const int   PIN_I_A = PA0, PIN_I_B = PA1, PIN_I_C = PA2;        // shunts de corrente (ADC)
static const int   PIN_VBUS = PA3,  PIN_NTC = PA4;                     // tensão do barramento / NTC
static const int   PIN_BRAKE = PB0;                                    // brake resistor (PWM)
static const float ENC_CPR   = 10000.0f;   // Omron E6B2-CWZ6C (2500 PPR × 4)
static const int   POLE_PAIRS = 20;        // motor de hoverboard (~15–20) — AJUSTAR
static const float SUPPLY_V   = 24.0f;     // variante 24V — casar com a fonte/ODESC

// ===================== SimpleFOC =====================
static BLDCMotor       motor(POLE_PAIRS);
static BLDCDriver3PWM  driver(PIN_PH_A, PIN_PH_B, PIN_PH_C, PIN_EN);
static Encoder         encoder(PIN_ENC_A, PIN_ENC_B, ENC_CPR);
static void doA() { encoder.handleA(); }
static void doB() { encoder.handleB(); }

// ===================== HAL: cérebro (interfaces) ↔ hardware =====================
struct FocEncoder : IEncoder {
    float positionRad() override      { return encoder.getAngle(); }
    float velocityRadPerSec() override { return encoder.getVelocity(); }
};
struct FocMotor : IMotor {
    void setTorque(float nm) override { motor.move(nm); }   // modo torque
    void disable() override           { motor.disable(); }
};
struct AdcCurrent : ICurrentSense {
    void readPhaseCurrents(float& ia, float& ib, float& ic) override {
        // AJUSTAR: (leitura - offset) × escala (A por LSB) conforme os shunts/ganho da ODESC.
        ia = (analogRead(PIN_I_A) - 2048) * 0.01f;
        ib = (analogRead(PIN_I_B) - 2048) * 0.01f;
        ic = (analogRead(PIN_I_C) - 2048) * 0.01f;
    }
};
struct AdcPower : IPowerSense {
    float busVoltage() override  { return analogRead(PIN_VBUS) * 0.01f; } // AJUSTAR: divisor resistivo
    float mosfetTempC() override { return 25.0f; }                        // AJUSTAR: NTC → °C
    float motorTempC() override  { return 25.0f; }
};
struct PwmBrake : IBrakeResistor {
    void setDuty(float d) override { analogWrite(PIN_BRAKE, (int)(d * 255.0f)); }
};

static FocEncoder halEnc;
static FocMotor   halMot;
static AdcCurrent halCur;
static AdcPower   halPow;
static PwmBrake   halBrake;

static FfbEngine engine;
static unsigned long lastMicros = 0;

void setup() {
    Serial.begin(115200);

    // Encoder + sensor do SimpleFOC.
    encoder.init();
    encoder.enableInterrupts(doA, doB);
    motor.linkSensor(&encoder);

    // Driver.
    driver.voltage_power_supply = SUPPLY_V;
    driver.init();
    motor.linkDriver(&driver);

    // Modo torque (a força FFB vira torque). No M1, torque via tensão (sem sense calibrado ainda).
    motor.torque_controller = TorqueControlType::voltage;
    motor.controller        = MotionControlType::torque;
    motor.init();
    motor.initFOC();

    // Config do cérebro — casada com BaseSettingId; valores CONSERVADORES no M1 (AJUSTAR na bancada).
    engine.force.maxTorqueNm   = 2.5f;
    engine.force.torqueLimitNm = 1.5f;     // teto baixo no M1 (segurança)
    engine.startup.cfg.busMinV = 20; engine.startup.cfg.busMaxV = 28;   // 24V
    engine.startup.cfg.alignSeconds = 0.5f; engine.startup.cfg.rampSeconds = 1.0f;
    engine.guard.overVoltageV  = 27; engine.guard.overTempC = 70;
    engine.effect.damperNmPerRadPerSec = 0.3f;   // amortecimento anti-tremor (do nosso estudo de estabilidade)
    engine.currentLimitA       = 6.0f;
    engine.enableRequested     = false;    // COMEÇA DESLIGADO — arma via serial ('1')

    lastMicros = micros();
    Serial.println("=== DriveLab M1 (esqueleto) — motor open-loop + cerebro. '1'=arma '0'=desarma ===");
}

void loop() {
    motor.loopFOC();   // malha FOC do SimpleFOC (o mais rápido possível)

    const unsigned long now = micros();
    const float dt = (now - lastMicros) * 1e-6f;
    lastMicros = now;

    // M1: sem jogo → força 0. O engine cuida da partida segura + proteção + (futuro) força.
    engine.step(dt, halEnc, halCur, halPow, halBrake, halMot);

    // Arme/desarme manual pela serial (segurança na bancada).
    if (Serial.available()) {
        const char c = Serial.read();
        if (c == '1') engine.enableRequested = true;
        if (c == '0') { engine.enableRequested = false; engine.guard.clearFault(); engine.startup.clearFault(); }
    }
}
