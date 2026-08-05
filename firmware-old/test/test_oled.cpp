// ============================================================================
//  DriveLab Firmware
//  test_oled.cpp — Testes de HOST do visor OLED: canvas/fonte, composição das telas e debounce do botão.
//  Roda sem placa: firmware-base/test/run.sh
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include "../lib/base_display/oled_canvas.h"
#include "../lib/base_display/oled_status.h"
#include "../lib/base_display/button_debounce.h"

#include <cstdio>

using namespace drivelab;

static int g_fails = 0, g_checks = 0;
#define CHECK(cond) do { ++g_checks; if (!(cond)) { ++g_fails; \
    std::printf("FAIL %s:%d: %s\n", __FILE__, __LINE__, #cond); } } while (0)

int main()
{
    // ----- canvas: drawChar copia as colunas da fonte no lugar certo -----
    {
        OledCanvas c; c.clear();
        c.drawChar(0, 0, 'A');
        const uint8_t* gA = fontGlyph('A');   // {0x7E,0x11,0x11,0x11,0x7E}
        for (int i = 0; i < kFontW; ++i) CHECK(c.buf[i] == gA[i]);
        CHECK(c.buf[kFontW] == 0x00);          // coluna de espaço após o glifo continua zerada

        // segundo caractere avança kFontAdvance (6)
        c.clear();
        c.drawText(0, 0, "AB");
        const uint8_t* gB = fontGlyph('B');
        CHECK(c.buf[kFontAdvance] == gB[0]);   // 'B' começa na coluna 6
    }

    // ----- canvas: página (row) endereça o bloco certo do framebuffer -----
    {
        OledCanvas c; c.clear();
        c.drawChar(3, 2, '0');
        const uint8_t* g0 = fontGlyph('0');
        CHECK(c.buf[2 * kOledW + 3] == g0[0]);  // página 2, coluna 3
        CHECK(c.buf[3] == 0x00);                // página 0 intacta
    }

    // ----- canvas: recorte no limite direito (não estoura o buffer) -----
    {
        OledCanvas c; c.clear();
        c.drawChar(kOledW - 2, 0, 'M');         // só 2 colunas cabem
        CHECK(c.buf[kOledW - 2] == fontGlyph('M')[0]);
        CHECK(c.buf[kOledW - 1] == fontGlyph('M')[1]);
        // não escreveu além da linha (buf[kOledW] pertence à página 1)
        CHECK(c.buf[kOledW] == 0x00);
    }

    // ----- oledWrapPage: normaliza índice (inclui negativo/estouro) -----
    {
        CHECK(oledWrapPage(0) == 0);
        CHECK(oledWrapPage(kOledNumPages) == 0);
        CHECK(oledWrapPage(kOledNumPages + 1) == 1);
        CHECK(oledWrapPage(-1) == kOledNumPages - 1);
    }

    // ----- renderStatus: cada página desenha seu cabeçalho + dados -----
    {
        OledStatus s;
        s.verMajor = 0; s.verMinor = 3; s.verPatch = 4;
        s.busMilliV = 24000; s.mcuTempC = 45; s.clippingPct = 0; s.angleDeciDeg = 123;

        OledCanvas c;
        renderStatus(c, s, 0);                   // GERAL
        CHECK(c.buf[0] == fontGlyph('G')[0]);    // "GERAL" começa com G
        CHECK(c.buf[2 * kOledW] == fontGlyph('B')[0]); // linha 2 "BUS ..." começa com B

        renderStatus(c, s, 1);                   // VOLANTE
        CHECK(c.buf[0] == fontGlyph('V')[0]);    // "VOLANTE" começa com V
        CHECK(c.buf[2 * kOledW] == fontGlyph('A')[0]); // linha 2 "ANG ..." começa com A

        renderStatus(c, s, 2);                   // SOBRE
        CHECK(c.buf[0] == fontGlyph('S')[0]);    // "SOBRE" começa com S

        // rodapé: aviso de plausibilidade tem prioridade sobre o estado da força
        s.voltageImplausible = true;
        renderStatus(c, s, 0);
        CHECK(c.buf[7 * kOledW] == fontGlyph('!')[0]); // linha 7 começa com "!"
        s.voltageImplausible = false; s.forceActive = false;
        renderStatus(c, s, 0);
        CHECK(c.buf[7 * kOledW] == fontGlyph('F')[0]); // "FORCA: OFF" começa com F
    }

    // ----- ButtonDebounce: chia é filtrado; 1 clique = 1 borda confirmada -----
    {
        ButtonDebounce btn;
        // bounce nos primeiros ms (< 30) não confirma
        CHECK(!btn.update(true, 0));
        CHECK(!btn.update(false, 5));
        CHECK(!btn.update(true, 10));
        // estável por >= 30ms desde a última mudança (t=10 → t>=40) confirma UMA vez
        CHECK(!btn.update(true, 39));
        CHECK(btn.update(true, 40));
        CHECK(btn.pressed());
        // segurar não gera nova borda
        CHECK(!btn.update(true, 100));
        // soltar e apertar de novo = nova borda
        CHECK(!btn.update(false, 110));
        CHECK(!btn.update(false, 139));
        CHECK(!btn.update(false, 140));      // solto confirmado (não é borda de press)
        CHECK(!btn.update(true, 150));
        CHECK(btn.update(true, 180));        // novo clique confirmado
    }

    std::printf("oled: %d checks, %d fails\n", g_checks, g_fails);
    return g_fails ? 1 : 0;
}
