// ============================================================================
//  DriveLab Firmware
//  cogging_store.h — (De)serialização do mapa de cogging para um blob (flash). Puro/host-testável.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// A tabela de cogging é CALIBRADA POR-MOTOR (o criador roda uma vez, na bancada) e mora na FLASH do
// dispositivo — vai junto com o volante pro comprador. Aqui só a parte PORTÁVEL e testável: empacotar/
// desempacotar a CoggingMap<N> num blob com magic+versão+N+CRC (valida contra flash corrompida). A
// leitura/escrita de fato na flash (EEPROM emulada) mora no A0Channel (ARDUINO); este header é puro.
#pragma once

#include <cstdint>
#include <cstddef>
#include <cstring>
#include "cogging.h"

namespace drivelab {

static constexpr uint32_t kCoggingMagic   = 0x31474F43; // "COG1" (LE)
static constexpr uint8_t  kCoggingVersion = 1;

// Layout do blob: [magic u32][version u8][N u16][table N*float][crc u32].
template <int N>
constexpr size_t coggingBlobSize() { return 4 + 1 + 2 + static_cast<size_t>(N) * 4 + 4; }

// CRC32 (IEEE 802.3, sem tabela) — valida a integridade do blob na flash.
inline uint32_t coggingCrc32(const uint8_t* d, size_t len) {
    uint32_t crc = 0xFFFFFFFFu;
    for (size_t i = 0; i < len; ++i) {
        crc ^= d[i];
        for (int b = 0; b < 8; ++b)
            crc = (crc >> 1) ^ (0xEDB88320u & static_cast<uint32_t>(-static_cast<int32_t>(crc & 1u)));
    }
    return ~crc;
}

/// Serializa `map` em `out`. Retorna os bytes escritos (0 se `out` for pequeno demais).
template <int N>
size_t packCogging(const CoggingMap<N>& map, uint8_t* out, size_t outLen) {
    const size_t need = coggingBlobSize<N>();
    if (outLen < need) return 0;
    size_t o = 0;
    uint32_t magic = kCoggingMagic; std::memcpy(out + o, &magic, 4); o += 4;
    out[o++] = kCoggingVersion;
    uint16_t n = static_cast<uint16_t>(N); std::memcpy(out + o, &n, 2); o += 2;
    std::memcpy(out + o, map.table, static_cast<size_t>(N) * sizeof(float)); o += static_cast<size_t>(N) * sizeof(float);
    uint32_t crc = coggingCrc32(out, o); std::memcpy(out + o, &crc, 4); o += 4;   // CRC cobre magic..table
    return o;
}

/// Desserializa + VALIDA (magic/versão/N/CRC). Retorna true e preenche `map` só se tudo bater.
template <int N>
bool unpackCogging(const uint8_t* in, size_t len, CoggingMap<N>& map) {
    if (len < coggingBlobSize<N>()) return false;
    size_t o = 0;
    uint32_t magic; std::memcpy(&magic, in + o, 4); o += 4;
    if (magic != kCoggingMagic) return false;
    if (in[o++] != kCoggingVersion) return false;
    uint16_t n; std::memcpy(&n, in + o, 2); o += 2;
    if (n != static_cast<uint16_t>(N)) return false;
    const uint8_t* tablePtr = in + o;
    o += static_cast<size_t>(N) * sizeof(float);
    uint32_t crcStored; std::memcpy(&crcStored, in + o, 4);
    if (coggingCrc32(in, o) != crcStored) return false;   // recomputa sobre magic..table
    std::memcpy(map.table, tablePtr, static_cast<size_t>(N) * sizeof(float));
    return true;
}

}  // namespace drivelab
