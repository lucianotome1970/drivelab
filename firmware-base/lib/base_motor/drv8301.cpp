// ============================================================================
//  DriveLab Firmware
//  drv8301.cpp — Classe de HARDWARE do gate driver DRV8301: reset via
//  EN_GATE, escrita/leitura de registro via SPI (soft-CS) e checagem de
//  fault. Usa o framing puro de drv8301.h (drv8301Frame/ControlReg1/2).
//
//  IMPORTANTE: este arquivo só PREPARA o DRV8301 (SPI de configuração) —
//  não gera PWM, não habilita FOC, não move o motor. O motor fica sem
//  energia até uma task futura ligar TIM1 6-PWM (fora do escopo daqui).
//
//  Sequência de init (fonte: ODrive firmware oficial,
//  Firmware/Drivers/DRV8301/drv8301.cpp, Drv8301::init()):
//    1. Reset: EN_GATE baixo -> aguarda >=20us (mínimo do datasheet p/ reset
//       completo; ODrive usa 40us) -> EN_GATE alto -> aguarda t_spi_ready
//       (ODrive usa 20ms; datasheet dá até 10ms — 20ms cobre com folga).
//    2. Escreve Control Register 1 CINCO vezes seguidas — comentário do
//       próprio ODrive: "the write operation tends to be ignored if only
//       done once (not sure why)".
//    3. Escreve Control Register 2 UMA vez.
//    4. Aguarda 100us para a config ser aplicada.
//    5. Lê os dois registros de volta e confere que batem com o que foi
//       escrito (0xbeef ou divergência = falha).
//    6. Lê Status Register 1/2 (get_error()) e falha se houver fault.
//
//  Config SPI (fonte: drv8301.cpp, spi_config_): 16 bits por word, MSB
//  first, CPOL=0/CPHA=1 (SPI Mode 1: HAL usa CLKPolarity=LOW,
//  CLKPhase=2EDGE), NSS por software (CS manual via GPIO) — replicado aqui
//  com SPI.beginTransaction(SPISettings(..., MSBFIRST, SPI_MODE1)) e
//  digitalWrite manual do pino de CS ao redor de cada transferência.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include <Arduino.h>
#include <SPI.h>

#include "drv8301.h"

// A DECLARAÇÃO da classe Drv8301 mora agora em drv8301.h (hoisted na Task 2
// para que src/m5/main.cpp consiga instanciá-la) — aqui só os corpos dos
// métodos, guardados por `#ifdef ARDUINO` no header. Comentários de fonte
// completos no topo do arquivo e junto de cada trecho abaixo.

void Drv8301::begin(SPIClass& spi, int csPin, int enGatePin, int nFaultPin, Drv8301Gain gain)
{
    spi_ = &spi;
    csPin_ = csPin;
    enGatePin_ = enGatePin;
    nFaultPin_ = nFaultPin;
    gain_ = gain;

    pinMode(csPin_, OUTPUT);
    digitalWrite(csPin_, HIGH); // CS idle alto (soft-NSS, ver spi_config_ do ODrive)
    pinMode(enGatePin_, OUTPUT);
    digitalWrite(enGatePin_, LOW); // mantém em reset até configure()
    pinMode(nFaultPin_, INPUT_PULLUP);

    ready_ = false;
}

bool Drv8301::configure()
{
    ready_ = false;

    // 1. Reset via EN_GATE (também reseta a interface SPI do chip).
    digitalWrite(enGatePin_, LOW);
    delayMicroseconds(40); // mínimo do datasheet p/ reset completo: 20us
    digitalWrite(enGatePin_, HIGH);
    delay(20); // t_spi_ready (datasheet: até 10ms; ODrive usa 20ms de folga)

    const uint16_t cr1 = drv8301ControlReg1();
    const uint16_t cr2 = drv8301ControlReg2(gain_);

    // 2. Control Register 1 cinco vezes (ver comentário do ODrive acima).
    for (int i = 0; i < 5; ++i)
    {
        if (!writeReg(kDrv8301RegControl1, cr1))
            return false;
    }

    // 3. Control Register 2 uma vez.
    if (!writeReg(kDrv8301RegControl2, cr2))
        return false;

    // 4. Aguarda a config ser aplicada.
    delayMicroseconds(100);

    // 5. Readback — confirma que os registros bateram com o que foi escrito.
    uint16_t rbCr1 = 0, rbCr2 = 0;
    if (!readReg(kDrv8301RegControl1, rbCr1) || rbCr1 != cr1)
        return false;
    if (!readReg(kDrv8301RegControl2, rbCr2) || rbCr2 != cr2)
        return false;

    // 6. Checa fault (Status Register 1/2) antes de declarar pronto.
    if (faulted())
        return false;

    ready_ = true;
    return true;
}

bool Drv8301::faulted()
{
    if (digitalRead(nFaultPin_) == LOW)
        return true;

    uint16_t status1 = 0, status2 = 0;
    if (!readReg(kDrv8301RegStatus1, status1))
        return true; // falha de comunicação SPI também conta como fault
    if (!readReg(kDrv8301RegStatus2, status2))
        return true;

    // Status Register 1: bits [10:0] = flags de fault (fonte: drv8301.hpp,
    // FaultType_e — todos os bits de Status1 são faults; NoFault = 0).
    // Status Register 2: só o bit [7] (GVDD_OV) é fault; os demais bits
    // desse registro são reservados/outra telemetria.
    return status1 != 0 || (status2 & 0x0080) != 0;
}

bool Drv8301::writeReg(uint8_t addr, uint16_t data)
{
    uint16_t frame = drv8301Frame(false, addr, data);
    transfer16(frame);
    delayMicroseconds(1);
    return true; // sem checagem de sucesso na escrita (mesmo comportamento do ODrive)
}

bool Drv8301::readReg(uint8_t addr, uint16_t& outData)
{
    // DRV8301: a leitura precisa da word de comando mandada duas vezes —
    // a primeira transação "arma" o endereço no chip, a resposta útil só
    // vem na segunda (mesmo padrão de Drv8301::read_reg() do ODrive).
    uint16_t cmd = drv8301Frame(true, addr, 0);
    transfer16(cmd);
    delayMicroseconds(1);
    uint16_t rx = transfer16(cmd);
    delayMicroseconds(1);

    if (rx == 0xbeef) // sentinela de erro do driver (ver drv8301.cpp do ODrive)
        return false;

    outData = drv8301FrameData(rx);
    return true;
}

uint16_t Drv8301::transfer16(uint16_t word)
{
    spi_->beginTransaction(SPISettings(2600000, MSBFIRST, SPI_MODE1));
    digitalWrite(csPin_, LOW);
    uint16_t rx = spi_->transfer16(word);
    digitalWrite(csPin_, HIGH);
    spi_->endTransaction();
    return rx;
}
