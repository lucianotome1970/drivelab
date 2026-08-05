// ============================================================================
//  DriveLab Firmware
//  oled_status.h — Compõe as telas de status do OLED (páginas cicladas por botão). Puro/host-testável.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Recebe os MESMOS números da telemetria (mV, deci-graus, %, etc.) e desenha o layout no OledCanvas.
// Várias PÁGINAS cicladas por um botão (GPIO3/PA2) — renderStatus(c, s, page). Formatação com inteiros
// (nada de %f: o newlib-nano do STM32duino não traz float no printf por padrão). Sem hardware; host-testável.
#pragma once

#include <cstdio>
#include <cstdint>
#include "oled_canvas.h"

namespace drivelab {

static constexpr int kOledNumPages = 3;   ///< Geral, Volante, Sobre

// Snapshot do estado a exibir (unidades cruas da telemetria).
struct OledStatus {
    uint8_t verMajor = 0, verMinor = 0, verPatch = 0;
    int  busMilliV = 0;          ///< tensão do barramento (mV) — BusVoltageMv
    int  fetTempC = -128;        ///< temperatura dos FETs (°C); -128 = sem sensor
    int  mcuTempC = 0;           ///< temperatura do MCU (°C)
    int  clippingPct = 0;        ///< clipping do FFB (0..100 %)
    int  angleDeciDeg = 0;       ///< ângulo do volante (0.1°), relativo ao centro
    bool forceActive = false;    ///< força/motor ativos (Stage 1)
    bool voltageImplausible = false; ///< aviso: tensão lida não bate com a variante
};

// Normaliza o índice de página p/ [0, kOledNumPages) (aceita negativo/estouro).
inline int oledWrapPage(int page) {
    return ((page % kOledNumPages) + kOledNumPages) % kOledNumPages;
}

// Cabeçalho: nome da página + "n/N" à direita.
inline void oledHeader(OledCanvas& c, const char* name, int page) {
    char idx[8];
    std::snprintf(idx, sizeof idx, "%d/%d", page + 1, kOledNumPages);
    c.textLine(0, name);
    c.drawText(108, 0, idx);
}

/// Desenha a tela de status da PÁGINA `page` (limpa antes). 21 col × 8 linhas com a fonte 5x7.
inline void renderStatus(OledCanvas& c, const OledStatus& s, int page) {
    c.clear();
    page = oledWrapPage(page);
    char b[24];

    switch (page) {
        case 0: {  // ---- GERAL ----
            oledHeader(c, "GERAL", 0);
            const int v10 = (s.busMilliV + 50) / 100;   // mV → 0.1V arredondado
            std::snprintf(b, sizeof b, "BUS   %d.%dV", v10 / 10, v10 % 10);
            c.textLine(2, b);
            // FET: -128 = sem sensor → mostra "--"
            if (s.fetTempC <= -128) std::snprintf(b, sizeof b, "FET   --");
            else                    std::snprintf(b, sizeof b, "FET   %dC", s.fetTempC);
            c.textLine(3, b);
            std::snprintf(b, sizeof b, "MCU   %dC", s.mcuTempC);
            c.textLine(4, b);
            std::snprintf(b, sizeof b, "CLIP  %d%%", s.clippingPct);
            c.textLine(5, b);
            break;
        }
        case 1: {  // ---- VOLANTE ----
            oledHeader(c, "VOLANTE", 1);
            const int a = s.angleDeciDeg, mag = a < 0 ? -a : a;
            std::snprintf(b, sizeof b, "ANG  %c%d.%d", a < 0 ? '-' : '+', mag / 10, mag % 10);
            c.textLine(2, b);
            std::snprintf(b, sizeof b, "CLIP  %d%%", s.clippingPct);
            c.textLine(4, b);
            break;
        }
        default: {  // ---- SOBRE ----
            oledHeader(c, "SOBRE", 2);
            c.textLine(2, "DRIVELAB");
            std::snprintf(b, sizeof b, "V%u.%u.%u", s.verMajor, s.verMinor, s.verPatch);
            c.textLine(3, b);
            c.textLine(5, "OPEN DD WHEEL");
            break;
        }
    }

    // Rodapé comum: aviso de plausibilidade tem prioridade; senão, estado da força.
    if (s.voltageImplausible)
        c.textLine(7, "! TENSAO/VARIANTE ?");
    else
        c.textLine(7, s.forceActive ? "FORCA: ATIVA" : "FORCA: OFF");
}

}  // namespace drivelab
