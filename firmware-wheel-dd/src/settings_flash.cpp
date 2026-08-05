// ============================================================================
//  DriveLab
//  settings_flash.cpp — Grava/lê o blob de settings A0 na região FFB_NVM (setor 1). STM32F4 HAL.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Região FFB_NVM reservada no linker (linker/STM32F405RGTx_FLASH.ld):
//   FFB_NVM (r) : ORIGIN = 0x08004000, LENGTH = 32K  (setores 1 e 2).
// Usamos o SETOR 1 (16K @ 0x08004000) — sobra folgada p/ o blob (~372 B); o setor 2 fica de reserva.
// O NVM do ODrive no F405 usa os setores 10/11 (0x080C0000/0x080E0000) p/ a cal do motor/encoder —
// então NÃO há colisão: escrever aqui nunca toca a calibração.
#include "settings_flash.h"
#include <stm32f4xx_hal.h>
#include <cstring>

static constexpr uint32_t kNvmBase   = 0x08004000UL;   // setor 1
static constexpr uint32_t kNvmSize   = 0x4000UL;       // 16 KiB
static constexpr uint32_t kNvmSector = FLASH_SECTOR_1;

extern "C" size_t settings_flash_read(uint8_t* buf, size_t len) {
    if (buf == nullptr) return 0;
    if (len > kNvmSize) len = kNvmSize;
    std::memcpy(buf, reinterpret_cast<const void*>(kNvmBase), len);   // flash memory-mapped
    return len;
}

extern "C" bool settings_flash_write(const uint8_t* buf, size_t len) {
    if (buf == nullptr || len == 0 || len > kNvmSize) return false;

    if (HAL_FLASH_Unlock() != HAL_OK) return false;

    // Apaga o setor 1 (obrigatório antes de reprogramar; flash só vai 1→0 sem erase).
    FLASH_EraseInitTypeDef er;
    std::memset(&er, 0, sizeof(er));
    er.TypeErase    = FLASH_TYPEERASE_SECTORS;
    er.Sector       = kNvmSector;
    er.NbSectors    = 1;
    er.VoltageRange = FLASH_VOLTAGE_RANGE_3;   // 2.7–3.6V → programação por word (32-bit)
    uint32_t sectorError = 0;
    if (HAL_FLASHEx_Erase(&er, &sectorError) != HAL_OK) {
        HAL_FLASH_Lock();
        return false;
    }

    // Grava por word (32-bit). Se `len` não for múltiplo de 4, o resto do último word fica 0xFF
    // (inofensivo: o blob tem tamanho fixo e o leitor valida por CRP/CRC).
    bool ok = true;
    for (size_t o = 0; o < len; o += 4) {
        uint32_t word = 0xFFFFFFFFu;
        size_t rem = (len - o) < 4 ? (len - o) : 4;
        std::memcpy(&word, buf + o, rem);
        if (HAL_FLASH_Program(FLASH_TYPEPROGRAM_WORD, kNvmBase + o, word) != HAL_OK) {
            ok = false;
            break;
        }
    }

    HAL_FLASH_Lock();
    return ok;
}
