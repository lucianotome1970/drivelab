// ============================================================================
//  DriveLab
//  settings_flash.h — I/O de flash da região FFB_NVM (blob de settings A0). Plataforma (STM32F4 HAL).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once
#include <cstdint>
#include <cstddef>

extern "C" {

// Copia o blob salvo da região FFB_NVM p/ `buf` (até `len` bytes; flash é memory-mapped, sempre lê).
// A VALIDAÇÃO (magic/CRC via unpackSettings) é do chamador. Retorna os bytes copiados.
size_t settings_flash_read(uint8_t* buf, size_t len);

// Apaga o setor da FFB_NVM e grava `len` bytes de `buf`. ⚠️ STALLA a CPU inteira durante o
// erase/program (~centenas de ms no F405 single-bank) → chamar SÓ com o motor IDLE, senão a ISR
// do controle perde deadline e desarma. Retorna true se erase+program OK.
bool settings_flash_write(const uint8_t* buf, size_t len);

}
