// ============================================================================
//  DriveLab Firmware
//  main.cpp (m5) — Task 2: env m5 + pinmap ODrive v3.6 + config do DRV8301.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// >>> MOTOR DESLIGADO NESTE ARQUIVO. <<<
//   Este é o PRIMEIRO passo do M5 real (ODrive v3.6 / MKS ODRIVE-S V3.6-S6V,
//   56V) — só prova que o toolchain compila drv8301.cpp + SimpleFOC juntos e
//   que o DRV8301 responde/configura via SPI3. Não há NENHUMA chamada que
//   produza PWM ou campo girante:
//     * `driver` (BLDCDriver6PWM), `motor` (BLDCMotor) e `encoder` (Encoder)
//       são só CONSTRUÍDOS (o construtor do SimpleFOC apenas guarda os
//       parâmetros/pinos — não toca hardware nenhum); NUNCA chamamos
//       driver.init(), motor.linkDriver()/init()/initFOC()/enable(),
//       encoder.init() ou motor.loopFOC()/move(). Ficam "mortos" neste
//       arquivo de propósito.
//     * Só o DRV8301 é de fato configurado (drv.begin()+drv.configure()),
//       que é SPI puro (config de registro) — não gera PWM nas fases.
//   USB/A0/engine (lib/brain) e o restante do SimpleFOC entram em tasks
//   futuras deste mesmo M5; aqui só CDC mínimo (TinyUSB) para log de status.

// IMPORTANTE — ordem de include: Adafruit_TinyUSB.h TEM que vir antes de
// Arduino.h/SimpleFOC.h. Arduino.h (WSerial.h, core STM32duino) define a
// macro `Serial` como `Serial4` (nome do UART default deste core); o
// Adafruit_TinyUSB.h tenta REDEFINIR `Serial` como `SerialTinyUSB` — se
// Arduino.h já rodou primeiro, as duas macros ficam mutuamente
// referenciadas (`Serial -> SerialTinyUSB -> Serial -> ...`) e a proteção
// de auto-referência do pré-processador C deixa o token `Serial` LITERAL
// (sem expandir para nenhum símbolo real) -- erro "'Serial' was not
// declared in this scope", inclusive dentro do header do SimpleFOC
// (SimpleFOCDebug.h usa `&Serial` como default de parâmetro). Incluindo
// Adafruit_TinyUSB.h primeiro, a macro `Serial` já está definida como
// `SerialTinyUSB` ANTES de Arduino.h tentar redefini-la — e como
// `USBD_USE_CDC` não está setado neste env (só `USE_TINYUSB`), WSerial.h
// não tenta redefinir Serial de novo (ver `#if defined(USBCON) &&
// defined(USBD_USE_CDC)` em WSerial.h), então a macro correta sobrevive.
// Usamos `SerialTinyUSB` (não `Serial`) no código deste arquivo mesmo assim,
// pelo mesmo motivo — é o padrão já usado em src/m05 e lib/base_usb.
#include <Adafruit_TinyUSB.h>
#include <Arduino.h>
#include <SimpleFOC.h>

#include "drv8301.h"
#include "odrive_v36_pins.h"

// ===================== Parâmetros do motor — AJUSTAR NA BANCADA =====================
// Pole pairs: motor de hoverboard in-wheel tipicamente ~15 pares de polos
// (30 ímãs) — "senso comum" da comunidade SimpleFOC/hoverboard, confiança
// MÉDIA (ver docs/superpowers/specs/2026-07-20-odrive-v36-driver-pinmap.md,
// seção 4.1). Confirmar com find_pole_pairs_number.ino do SimpleFOC ou
// contando detentes magnéticos na bancada antes de confiar neste valor.
static const int   POLE_PAIRS = 15; // AJUSTAR na bancada
static const float ENC_CPR    = 8192.0f; // AJUSTAR na bancada (encoder real do eixo M0)
static const float SUPPLY_V   = 56.0f;   // MKS ODRIVE-S V3.6-S6V — variante 56V (NÃO 24V)

// ===================== SPI3 (compartilhado pelos dois DRV8301 no ODrive v3.6) =====================
// SPIClass(mosi, miso, sck) — construtor do STM32duino para SPI fora do
// barramento default. Pinos: ver odrive_v36_pins.h / spec de pinmap.
static SPIClass spi3(kOdrivePinSpiMosi, kOdrivePinSpiMiso, kOdrivePinSpiSck);

// ===================== DRV8301 (gate driver do eixo M0) =====================
// Ganho do amp de shunt: G20 (20 V/V) como default conservador — mesmo
// default do construtor Drv8301Gain do header; a calibração real de
// corrente (ganho x resistência do shunt x offset do ADC) é trabalho de
// bring-up futuro (ver spec, seção 4.2), não deste passo.
static Drv8301 drv;

// ===================== SimpleFOC — construídos, NUNCA inicializados aqui =====================
// BLDCDriver6PWM(phA_h, phA_l, phB_h, phB_l, phC_h, phC_l, en) — 6-PWM (não
// 3-PWM: o DRV8301 recebe high-side E low-side independentes, dead-time
// inserido pelo próprio timer TIM1/BDTR do STM32, ver spec seção 1.1).
static BLDCDriver6PWM driver(kOdrivePinPhaseAH, kOdrivePinPhaseAL,
                              kOdrivePinPhaseBH, kOdrivePinPhaseBL,
                              kOdrivePinPhaseCH, kOdrivePinPhaseCL,
                              kOdrivePinEnGate);
static BLDCMotor motor(POLE_PAIRS);
static Encoder   encoder(kOdrivePinEncoderA, kOdrivePinEncoderB, ENC_CPR, kOdrivePinEncoderZ);

void setup()
{
    // CDC mínimo (TinyUSB) só para log de status — SEM HID/FFB/A0 (isso é
    // trabalho de tasks futuras, ver lib/base_usb/usb_base.{h,cpp} para o
    // padrão completo quando este M5 for ligar o canal FFB de verdade).
    // "Serial" já é a CDC do TinyUSB neste core (mesma nota de
    // lib/base_usb/usb_base.cpp) — só precisa do begin() da pilha.
    if (!TinyUSBDevice.isInitialized())
    {
        TinyUSBDevice.begin(0);
    }
    SerialTinyUSB.begin(115200);

    // Só armazena os parâmetros do driver (voltage_power_supply) — NÃO
    // chama driver.init() aqui (isso configuraria os timers/PWM; mesmo que
    // o duty inicial fosse 0%, este passo prefere não tocar em TIM1 de
    // jeito nenhum ainda). driver/motor/encoder ficam só construídos.
    driver.voltage_power_supply = SUPPLY_V;

    // ---- DRV8301: único hardware de fato configurado neste passo (SPI puro,
    // sem PWM) ----
    spi3.begin();
    drv.begin(spi3, kOdrivePinCsM0, kOdrivePinEnGate, kOdrivePinNFault, Drv8301Gain::G20);
    const bool ok = drv.configure();

    // Log de status/fault — TinyUSBDevice ainda pode não estar "mounted" tão
    // cedo em setup(); os primeiros prints podem ser descartados (mesmo
    // comportamento documentado em src/m05/main.cpp). O heartbeat do loop()
    // repete o status para garantir que apareça assim que o host montar.
    SerialTinyUSB.printf("DriveLab M5 (Task 2) — DRV8301 configure()=%s ready=%s faulted=%s | motor OFF\n",
                  ok ? "OK" : "FAIL",
                  drv.isReady() ? "true" : "false",
                  drv.faulted() ? "true" : "false");
}

void loop()
{
    // Mantém a pilha TinyUSB viva (necessário mesmo sem HID registrado).
    TinyUSBDevice.task();

    if (!TinyUSBDevice.mounted())
    {
        delay(2);
        return;
    }

    // Heartbeat de status a ~1 Hz — nenhuma chamada de FOC/PWM aqui.
    // motor/driver/encoder permanecem só construídos, nunca .init()/
    // .initFOC()/.enable()/.loopFOC() — motor continua sem energia.
    static uint32_t lastLog = 0;
    const uint32_t now = millis();
    if (now - lastLog >= 1000)
    {
        lastLog = now;
        SerialTinyUSB.printf("M5 heartbeat — DRV8301 ready=%s faulted=%s | motor OFF (sem init/enable)\n",
                      drv.isReady() ? "true" : "false",
                      drv.faulted() ? "true" : "false");
    }
}
