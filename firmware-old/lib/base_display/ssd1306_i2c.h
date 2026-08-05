// ============================================================================
//  DriveLab Firmware
//  ssd1306_i2c.h — Driver do OLED SSD1306 por I2C bit-bang (PA0=SDA/PA1=SCL). Só ARDUINO.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// A ODrive/MKS não expõe o I2C de hardware no header GPIO, então fazemos I2C por SOFTWARE (bit-bang) em
// dois GPIOs livres do header (GPIO1=PA0=SDA, GPIO2=PA1=SCL). Open-drain emulado: "solta" a linha =
// INPUT_PULLUP (o módulo OLED tem pull-up); "puxa baixo" = OUTPUT LOW. O RENDER (framebuffer/fonte/layout)
// é puro em oled_canvas.h/oled_status.h; aqui só o transporte. Guardado por ARDUINO (usa Arduino.h).
//
// ROBUSTEZ: begin() detecta presença (ACK do endereço). Se o OLED não responder, m_present=false e todos
// os métodos viram NO-OP — o firmware NUNCA trava por causa de um display ausente/mal ligado.
#pragma once

#ifdef ARDUINO

#include <cstdint>
#include "oled_canvas.h"

namespace drivelab {

class Ssd1306I2c {
public:
    // sdaPin/sclPin: pinos Arduino (ex.: PA0/PA1). addr7 = endereço I2C 7-bit (0x3C típico).
    Ssd1306I2c(int sdaPin, int sclPin, uint8_t addr7 = 0x3C)
        : m_sda(sdaPin), m_scl(sclPin), m_addr7(addr7) {}

    // Inicializa o SSD1306 128x64. Retorna true se o display respondeu (ACK). Se false, vira NO-OP.
    bool begin();

    bool present() const { return m_present; }

    // Envia o framebuffer inteiro (1024 bytes). NO-OP se o display não estiver presente.
    void flush(const OledCanvas& c);

private:
    // --- bit-bang I2C ---
    void sdaHigh(); void sdaLow();
    void sclHigh(); void sclLow();
    int  readSda();
    void i2cStart(); void i2cStop();
    bool i2cWrite(uint8_t b);        // retorna true se recebeu ACK
    bool cmd(uint8_t c);             // envia 1 comando (control 0x00)
    bool cmd2(uint8_t c, uint8_t a); // comando + argumento

    int     m_sda;
    int     m_scl;
    uint8_t m_addr7;
    bool    m_present = false;
};

}  // namespace drivelab

#endif  // ARDUINO
