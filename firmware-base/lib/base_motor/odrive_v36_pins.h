// ============================================================================
//  DriveLab Firmware
//  odrive_v36_pins.h — Pinmap do ODrive v3.6 (placa real: MKS ODRIVE-S
//  V3.6-S6V, um clone STM32F405-based do ODrive v3.6 genuíno), eixo M0.
//
//  Fonte: docs/superpowers/specs/2026-07-20-odrive-v36-driver-pinmap.md
//  (confiança ALTA — cross-check contra o firmware oficial da ODrive
//  Robotics, mesma família de fonte usada em lib/base_motor/drv8301.*).
//  Confirmar com multímetro/continuidade na bancada antes do primeiro
//  bring-up com motor conectado (clones podem divergir do genuíno em
//  detalhes de silkscreen/roteamento, mesmo com o mesmo MCU).
//
//  ATUALIZAÇÃO (M5 Task 4): pinos de fase/DRV8301/encoder/sense abaixo
//  CONFIRMADOS pela fonte de fábrica MKS v0.5.1 (board_config_v3.h +
//  main.h, ~/Downloads/MKS_ODrive_S-fw-v0.5.1) — cross-check direto contra
//  o firmware que roda de fábrica na MKS ODRIVE-S V3.6-S6V, não só o
//  ODrive genuíno. O brake resistor (antes sentinela -1) também foi
//  confirmado nesta fonte — ver bloco "Brake resistor" abaixo.
//
//  NÃO confundir com src/m1/main.cpp — aquele esqueleto usa pinos
//  PLACEHOLDER de uma ODESC v4.2 (3-PWM, EN único, shunts/VBUS diferentes,
//  24V) que NÃO se aplicam à ODrive v3.6 / MKS ODRIVE-S V3.6-S6V (6-PWM,
//  EN_GATE único mas layout de fase diferente, 56V).
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

// ----------------------------------------------------------------------------
// Fases do motor M0 — 6-PWM (TIM1), high-side + low-side independentes.
// Confiança: ALTA — CONFIRMADO pela fonte de fábrica MKS v0.5.1
// (board_config_v3.h + main.h), além do cross-check original com o
// firmware oficial ODrive.
// ----------------------------------------------------------------------------
static constexpr int kOdrivePinPhaseAH = PA8;   // TIM1_CH1
static constexpr int kOdrivePinPhaseBH = PA9;   // TIM1_CH2
static constexpr int kOdrivePinPhaseCH = PA10;  // TIM1_CH3
static constexpr int kOdrivePinPhaseAL = PB13;  // TIM1_CH1N
static constexpr int kOdrivePinPhaseBL = PB14;  // TIM1_CH2N
static constexpr int kOdrivePinPhaseCL = PB15;  // TIM1_CH3N

// ----------------------------------------------------------------------------
// Gate driver DRV8301 (M0) — enable + fault + SPI3 dedicado + CS do M0.
// Confiança: ALTA — CONFIRMADO pela fonte de fábrica MKS v0.5.1
// (board_config_v3.h + main.h).
// ----------------------------------------------------------------------------
static constexpr int kOdrivePinEnGate  = PB12;  // EN_GATE (comum às 6 fases)
static constexpr int kOdrivePinNFault  = PD2;   // nFAULT (ativo baixo)
static constexpr int kOdrivePinSpiSck  = PC10;  // SPI3_SCK
static constexpr int kOdrivePinSpiMiso = PC11;  // SPI3_MISO
static constexpr int kOdrivePinSpiMosi = PC12;  // SPI3_MOSI
static constexpr int kOdrivePinCsM0    = PC13;  // CS do DRV8301 do eixo M0 (soft-NSS)

// ----------------------------------------------------------------------------
// Encoder incremental M0 — A/B em TIM3 (contagem em hardware) + índice Z.
// Confiança: ALTA — CONFIRMADO pela fonte de fábrica MKS v0.5.1
// (board_config_v3.h + main.h).
// ----------------------------------------------------------------------------
static constexpr int kOdrivePinEncoderA = PB4;  // TIM3_CH1
static constexpr int kOdrivePinEncoderB = PB5;  // TIM3_CH2
static constexpr int kOdrivePinEncoderZ = PC9;  // índice (não usado pelo Encoder básico do SimpleFOC)

// ----------------------------------------------------------------------------
// Sensing analógico M0 — shunts de corrente (fases B/C; a fase A é
// reconstruída por KCL, mesmo esquema do ODrive) + barramento + NTC.
// Confiança: ALTA — CONFIRMADO pela fonte de fábrica MKS v0.5.1
// (board_config_v3.h + main.h). Ganho do amp de shunt: 40 V/V (G40) é o
// valor que o firmware de fábrica seleciona para o shunt de 500µΩ + faixa
// de 60A desta placa (ver drv8301.h/Drv8301Gain e FocCurrent, motor_hal.*).
// ----------------------------------------------------------------------------
static constexpr int kOdrivePinShuntIB = PC0;
static constexpr int kOdrivePinShuntIC = PC1;
static constexpr int kOdrivePinVBus    = PA6;
static constexpr int kOdrivePinNtcM0   = PC5;

// ----------------------------------------------------------------------------
// Brake resistor — dissipa a energia de regeneração via um MEIO-PONTE
// (half-bridge) dedicado, não um único MOSFET low-side como a spec de
// pinmap original supôs. CONFIRMADO pela fonte de fábrica MKS v0.5.1
// (board_config_v3.h + main.h): AUX_L = PB10 / AUX_H = PB11, ambos em TIM2
// (mesmo timer que a spec anterior já apontava — TIM2_CH3/CH4 — agora com
// os GPIOs físicos resolvidos). Confiança: ALTA (pinos), mas a PWM própria
// desse meio-ponte (canais/prescaler/dead-time do TIM2 para AUX_L/AUX_H)
// ainda não foi portada — fica para uma task futura de bancada. Por isso
// FocBrake::setDuty() (lib/base_motor/motor_hal.cpp) continua NO-OP no v1
// independente destes pinos estarem confirmados: a proteção de sobretensão
// (PowerGuard) e a partida conservadora (torque baixo, rampa) já cobrem a
// segurança sem depender do brake resistor nesta fase.
// ----------------------------------------------------------------------------
static constexpr int kOdrivePinAuxBrakeL = PB10;  // TIM2_CH3 — AUX_L (half-bridge low-side)
static constexpr int kOdrivePinAuxBrakeH = PB11;  // TIM2_CH4 — AUX_H (half-bridge high-side)
