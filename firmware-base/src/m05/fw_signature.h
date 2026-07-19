// ============================================================================
//  DriveLab Firmware
//  fw_signature.h — Assinatura embutida no binário (.bin) da base, usada pelo
//  host (app / check_fw_signature.py) pra validar que um arquivo de firmware
//  selecionado pra atualização por USB realmente é um firmware DriveLab e
//  bate com o "kind" do dispositivo alvo (Base/Pedal/Handbrake/Wheel).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Layout (12 bytes, sem padding relevante — todos os campos são char/uint8_t):
//   magic[8] : ASCII "DRVLABFW", SEM terminador NUL (char[8] com inicializador
//              por caractere, não string literal — string literal viraria
//              9 bytes com o NUL implícito no fim).
//   kind     : 1 = Base, 2 = Pedal, 3 = Handbrake, 4 = Wheel (ver
//              firmware-*/tools/check_fw_signature.py e o enum espelho no
//              app, quando existir).
//   ver[3]   : major, minor, patch do firmware (informativo por ora).
//
// Retenção no .bin: `__attribute__((used))` impede o linker/otimizador de
// descartar o símbolo por "não referenciado" (ele SERIA descartado sem isso,
// já que nenhum código lê o valor em runtime). Também referenciamos
// fw_signature explicitamente no main.cpp (setup()) como reforço/documentação
// — ver comentário lá.
#pragma once

#include <cstdint>

// Versão do firmware — FONTE ÚNICA. Usada tanto na assinatura do .bin (validação
// de update) quanto na telemetria DeviceState 0x21 (o que o app mostra como
// "firmware vX.Y.Z"). Mantenha os dois em sincronia bumpando SÓ aqui.
#define DRVLAB_FW_VER_MAJOR 0
#define DRVLAB_FW_VER_MINOR 2
#define DRVLAB_FW_VER_PATCH 0

struct FwSignature {
    char magic[8];
    uint8_t kind;
    uint8_t ver[3];
};

__attribute__((used)) static const FwSignature fw_signature = {
    {'D', 'R', 'V', 'L', 'A', 'B', 'F', 'W'}, /*Base*/ 1,
    {DRVLAB_FW_VER_MAJOR, DRVLAB_FW_VER_MINOR, DRVLAB_FW_VER_PATCH}
};
