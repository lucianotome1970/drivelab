// ============================================================================
//  DriveLab Firmware
//  ssd1306_i2c.cpp — Corpo do driver SSD1306 por I2C bit-bang. Só ARDUINO.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#ifdef ARDUINO

#include <Arduino.h>
#include "ssd1306_i2c.h"

namespace drivelab {

// Meio-período do clock I2C. ~2µs → ~250kHz — conservador p/ OLED clone e p/ o bit-bang. AJUSTAR se preciso.
static inline void i2cDelay() { delayMicroseconds(2); }

// Open-drain emulado: "solta" = INPUT_PULLUP (linha sobe pelo pull-up do módulo); "baixo" = OUTPUT LOW.
void Ssd1306I2c::sdaHigh() { pinMode(m_sda, INPUT_PULLUP); }
void Ssd1306I2c::sdaLow()  { pinMode(m_sda, OUTPUT); digitalWrite(m_sda, LOW); }
void Ssd1306I2c::sclHigh() { pinMode(m_scl, INPUT_PULLUP); }
void Ssd1306I2c::sclLow()  { pinMode(m_scl, OUTPUT); digitalWrite(m_scl, LOW); }
int  Ssd1306I2c::readSda() { return digitalRead(m_sda); }

void Ssd1306I2c::i2cStart() {
    sdaHigh(); sclHigh(); i2cDelay();
    sdaLow();  i2cDelay();          // SDA cai com SCL alto = START
    sclLow();  i2cDelay();
}

void Ssd1306I2c::i2cStop() {
    sdaLow();  sclHigh(); i2cDelay();
    sdaHigh(); i2cDelay();          // SDA sobe com SCL alto = STOP
}

bool Ssd1306I2c::i2cWrite(uint8_t b) {
    for (int i = 0; i < 8; ++i) {
        if (b & 0x80) sdaHigh(); else sdaLow();
        b <<= 1;
        i2cDelay();
        sclHigh(); i2cDelay();      // clock alto = bit válido
        sclLow();  i2cDelay();
    }
    // 9º clock = ACK: solta SDA e amostra (0 = ACK do escravo)
    sdaHigh(); i2cDelay();
    sclHigh(); i2cDelay();
    const bool ack = (readSda() == 0);
    sclLow();  i2cDelay();
    return ack;
}

bool Ssd1306I2c::cmd(uint8_t c) {
    i2cStart();
    bool ok = i2cWrite(static_cast<uint8_t>(m_addr7 << 1));  // write
    ok = i2cWrite(0x00) && ok;                               // control: comando
    ok = i2cWrite(c) && ok;
    i2cStop();
    return ok;
}

bool Ssd1306I2c::cmd2(uint8_t c, uint8_t a) {
    i2cStart();
    bool ok = i2cWrite(static_cast<uint8_t>(m_addr7 << 1));
    ok = i2cWrite(0x00) && ok;
    ok = i2cWrite(c) && ok;
    ok = i2cWrite(a) && ok;
    i2cStop();
    return ok;
}

bool Ssd1306I2c::begin() {
    sdaHigh(); sclHigh();
    delay(5);

    // Sonda de presença: só o endereço. Sem ACK → OLED ausente → tudo vira NO-OP (nunca trava).
    i2cStart();
    const bool ack = i2cWrite(static_cast<uint8_t>(m_addr7 << 1));
    i2cStop();
    if (!ack) { m_present = false; return false; }

    // Sequência de init padrão SSD1306 128x64.
    cmd(0xAE);              // display off
    cmd2(0xD5, 0x80);       // clock
    cmd2(0xA8, 0x3F);       // multiplex = 64
    cmd2(0xD3, 0x00);       // offset 0
    cmd(0x40);              // start line 0
    cmd2(0x8D, 0x14);       // charge pump ON
    cmd2(0x20, 0x00);       // addressing: horizontal
    cmd(0xA1);              // segment remap
    cmd(0xC8);              // COM scan dec
    cmd2(0xDA, 0x12);       // COM pins
    cmd2(0x81, 0xCF);       // contraste
    cmd2(0xD9, 0xF1);       // pré-carga
    cmd2(0xDB, 0x40);       // VCOM
    cmd(0xA4);              // resume da RAM
    cmd(0xA6);              // normal (não invertido)
    cmd(0xAF);              // display ON

    m_present = true;
    return true;
}

void Ssd1306I2c::flush(const OledCanvas& c) {
    if (!m_present) return;

    cmd2(0x21, 0x00); cmd(0x7F);   // faixa de colunas 0..127 (0x21 col_start; depois col_end)
    cmd2(0x22, 0x00); cmd(0x07);   // faixa de páginas 0..7

    // Uma transação de dados com os 1024 bytes (control 0x40 = dados).
    i2cStart();
    i2cWrite(static_cast<uint8_t>(m_addr7 << 1));
    i2cWrite(0x40);
    for (int i = 0; i < kOledBytes; ++i) i2cWrite(c.buf[i]);
    i2cStop();
}

}  // namespace drivelab

#endif  // ARDUINO
