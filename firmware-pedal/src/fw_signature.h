// ============================================================================
//  DriveLab Firmware — Pedaleira
//  fw_signature.h — Assinatura embutida no binário (.uf2) da pedaleira, usada
//  pelo host (app / check_fw_signature.py) pra validar que um arquivo de
//  firmware selecionado pra atualização por USB (UF2/BOOTSEL) realmente é um
//  firmware DriveLab e bate com o "kind" do dispositivo alvo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Layout (12 bytes, sem padding relevante — todos os campos são char/uint8_t):
//   magic[8] : ASCII "DRVLABFW", SEM terminador NUL (char[8] com inicializador
//              por caractere, não string literal — string literal viraria
//              9 bytes com o NUL implícito no fim).
//   kind     : 1 = Base, 2 = Pedal, 3 = Handbrake, 4 = Wheel.
//   ver[3]   : major, minor, patch do firmware (informativo por ora).
//
// Retenção no .uf2: `__attribute__((used))` impede o linker/otimizador de
// descartar o símbolo por "não referenciado". Reforçamos com um keepalive
// explícito no main.cpp (setup()) pra sobreviver ao `--gc-sections`.
#pragma once

#include <cstdint>

// Versão do firmware da pedaleira.
#define DRVLAB_FW_VER_MAJOR 0
#define DRVLAB_FW_VER_MINOR 1
#define DRVLAB_FW_VER_PATCH 0

struct FwSignature {
    char magic[8];
    uint8_t kind;
    uint8_t ver[3];
};

__attribute__((used)) static const FwSignature fw_signature = {
    {'D', 'R', 'V', 'L', 'A', 'B', 'F', 'W'}, /*Pedal*/ 2,
    {DRVLAB_FW_VER_MAJOR, DRVLAB_FW_VER_MINOR, DRVLAB_FW_VER_PATCH}
};
