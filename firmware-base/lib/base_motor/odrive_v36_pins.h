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
// Confiança: ALTA (spec de pinmap, cross-check com firmware oficial ODrive).
// ----------------------------------------------------------------------------
static constexpr int kOdrivePinPhaseAH = PA8;   // TIM1_CH1
static constexpr int kOdrivePinPhaseBH = PA9;   // TIM1_CH2
static constexpr int kOdrivePinPhaseCH = PA10;  // TIM1_CH3
static constexpr int kOdrivePinPhaseAL = PB13;  // TIM1_CH1N
static constexpr int kOdrivePinPhaseBL = PB14;  // TIM1_CH2N
static constexpr int kOdrivePinPhaseCL = PB15;  // TIM1_CH3N

// ----------------------------------------------------------------------------
// Gate driver DRV8301 (M0) — enable + fault + SPI3 dedicado + CS do M0.
// Confiança: ALTA.
// ----------------------------------------------------------------------------
static constexpr int kOdrivePinEnGate  = PB12;  // EN_GATE (comum às 6 fases)
static constexpr int kOdrivePinNFault  = PD2;   // nFAULT (ativo baixo)
static constexpr int kOdrivePinSpiSck  = PC10;  // SPI3_SCK
static constexpr int kOdrivePinSpiMiso = PC11;  // SPI3_MISO
static constexpr int kOdrivePinSpiMosi = PC12;  // SPI3_MOSI
static constexpr int kOdrivePinCsM0    = PC13;  // CS do DRV8301 do eixo M0 (soft-NSS)

// ----------------------------------------------------------------------------
// Encoder incremental M0 — A/B em TIM3 (contagem em hardware) + índice Z.
// Confiança: ALTA.
// ----------------------------------------------------------------------------
static constexpr int kOdrivePinEncoderA = PB4;  // TIM3_CH1
static constexpr int kOdrivePinEncoderB = PB5;  // TIM3_CH2
static constexpr int kOdrivePinEncoderZ = PC9;  // índice (não usado pelo Encoder básico do SimpleFOC)

// ----------------------------------------------------------------------------
// Sensing analógico M0 — shunts de corrente (fases B/C; a fase A é
// reconstruída por KCL, mesmo esquema do ODrive) + barramento + NTC.
// Confiança: ALTA (shunts/VBUS/NTC); ainda NÃO usados neste firmware (Task 2
// só configura o DRV8301, o sense de corrente entra em task futura).
// ----------------------------------------------------------------------------
static constexpr int kOdrivePinShuntIB = PC0;
static constexpr int kOdrivePinShuntIC = PC1;
static constexpr int kOdrivePinVBus    = PA6;
static constexpr int kOdrivePinNtcM0   = PC5;
