// ============================================================================
//  DriveLab Firmware
//  oled_canvas.h — Framebuffer 128x64 do SSD1306 + desenho de texto. Puro/host-testável.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// O framebuffer segue o layout NATIVO do SSD1306: 8 páginas de 128 bytes; cada byte = 8 pixels
// verticais (bit 0 em cima). Por isso a fonte column-major (oled_font5x7.h) entra por cópia direta.
// Sem hardware aqui — o flush por I2C mora em ssd1306_i2c.{h,cpp}. Testável no host.
#pragma once

#include <cstdint>
#include <cstring>
#include "oled_font5x7.h"

namespace drivelab {

static constexpr int kOledW     = 128;
static constexpr int kOledH     = 64;
static constexpr int kOledPages = kOledH / 8;                 // 8
static constexpr int kOledBytes = kOledW * kOledPages;        // 1024

struct OledCanvas {
    uint8_t buf[kOledBytes];

    void clear() { std::memset(buf, 0, sizeof(buf)); }

    // Desenha um glifo na coluna x (pixel) e página row (0..7). Recorta no limite direito.
    void drawChar(int x, int row, char c) {
        if (row < 0 || row >= kOledPages) return;
        const uint8_t* g = fontGlyph(c);
        uint8_t* page = &buf[row * kOledW];
        for (int i = 0; i < kFontW; ++i) {
            const int col = x + i;
            if (col < 0 || col >= kOledW) continue;
            page[col] = g[i];
        }
    }

    // Escreve uma string a partir de (x, row). Avança kFontAdvance por caractere. Retorna o x final.
    int drawText(int x, int row, const char* s) {
        while (*s) {
            drawChar(x, row, *s++);
            x += kFontAdvance;
        }
        return x;
    }

    // Conveniência: linha de texto por índice de página (0..7).
    void textLine(int row, const char* s) { drawText(0, row, s); }
};

}  // namespace drivelab
