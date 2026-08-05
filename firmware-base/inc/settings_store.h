// ============================================================================
//  DriveLab
//  settings_store.h — (De)serialização dos settings A0 da base num blob (flash). Puro/host-testável.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Os settings do app (força, DOR, ganhos, etc.) moram na FLASH da base pra sobreviver ao reboot
// (BaseCommand.SaveSettings). Aqui só a parte PORTÁVEL e testável: empacotar/desempacotar os dois
// arrays de valores (int em `ival`, float em `fval`, indexados por BaseSettingId) num blob com
// magic+versão+CRC (valida contra flash apagada/corrompida). A leitura/escrita de fato na flash
// (região FFB_NVM, setores 1/2) mora em settings_flash.cpp; este header é puro. Espelha o padrão
// provado do cogging_store.h do firmware-old.
#pragma once
#include <cstdint>
#include <cstddef>
#include <cstring>

namespace drivelab {

static constexpr uint32_t kSettingsMagic   = 0x31534C44; // "DLS1" (LE)
static constexpr uint16_t kSettingsVersion = 1;

// Layout do blob: [magic u32][version u16][count u16][ival count*i32 LE][fval count*f32 LE][crc u32].
// O CRC cobre de magic até o fim de fval (tudo menos o próprio CRC).
inline constexpr size_t settingsBlobSize(uint16_t count) {
    return 4 + 2 + 2 + static_cast<size_t>(count) * 4 + static_cast<size_t>(count) * 4 + 4;
}

// CRC32 (IEEE 802.3, sem tabela) — idêntico ao cogging_store, valida a integridade do blob.
inline uint32_t settingsCrc32(const uint8_t* d, size_t len) {
    uint32_t crc = 0xFFFFFFFFu;
    for (size_t i = 0; i < len; ++i) {
        crc ^= d[i];
        for (int b = 0; b < 8; ++b)
            crc = (crc >> 1) ^ (0xEDB88320u & static_cast<uint32_t>(-static_cast<int32_t>(crc & 1u)));
    }
    return ~crc;
}

/// Serializa `ival`/`fval` (count campos cada) em `out`. Retorna os bytes escritos (0 se `out`
/// for pequeno demais). Little-endian explícito (não depende do memcpy de struct).
inline size_t packSettings(const int32_t* ival, const float* fval, uint16_t count,
                           uint8_t* out, size_t outLen) {
    const size_t need = settingsBlobSize(count);
    if (out == nullptr || outLen < need) return 0;
    size_t o = 0;
    uint32_t magic = kSettingsMagic;   std::memcpy(out + o, &magic, 4);   o += 4;
    uint16_t ver   = kSettingsVersion; std::memcpy(out + o, &ver, 2);     o += 2;
    std::memcpy(out + o, &count, 2);   o += 2;
    for (uint16_t i = 0; i < count; ++i) { std::memcpy(out + o, &ival[i], 4); o += 4; }
    for (uint16_t i = 0; i < count; ++i) { std::memcpy(out + o, &fval[i], 4); o += 4; }
    uint32_t crc = settingsCrc32(out, o); std::memcpy(out + o, &crc, 4);   o += 4;
    return o;
}

/// Desserializa + VALIDA (magic/versão/count/CRC). Retorna true e preenche `ival`/`fval` só se
/// tudo bater. `count` = quantos campos o firmware ESPERA (o blob tem que ter o mesmo count).
inline bool unpackSettings(const uint8_t* in, size_t len, int32_t* ival, float* fval, uint16_t count) {
    if (in == nullptr || len < settingsBlobSize(count)) return false;
    size_t o = 0;
    uint32_t magic; std::memcpy(&magic, in + o, 4); o += 4;
    if (magic != kSettingsMagic) return false;
    uint16_t ver; std::memcpy(&ver, in + o, 2); o += 2;
    if (ver != kSettingsVersion) return false;
    uint16_t n; std::memcpy(&n, in + o, 2); o += 2;
    if (n != count) return false;   // mudou o nº de campos → blob antigo, ignora (usa defaults)
    const uint8_t* ivalPtr = in + o; o += static_cast<size_t>(count) * 4;
    const uint8_t* fvalPtr = in + o; o += static_cast<size_t>(count) * 4;
    uint32_t crcStored; std::memcpy(&crcStored, in + o, 4);
    if (settingsCrc32(in, o) != crcStored) return false;   // recomputa sobre magic..fval
    for (uint16_t i = 0; i < count; ++i) std::memcpy(&ival[i], ivalPtr + i * 4, 4);
    for (uint16_t i = 0; i < count; ++i) std::memcpy(&fval[i], fvalPtr + i * 4, 4);
    return true;
}

}  // namespace drivelab
