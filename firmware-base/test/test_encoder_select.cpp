// ============================================================================
//  DriveLab Firmware
//  test_encoder_select.cpp — Teste host da seleção de encoder (encoderType →
//  {kind, supported}). Puro, sem placa. v1: só ABI é suportado; magnéticos → M5.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include <cassert>
#include <cstdio>
#include "encoder_select.h"

int main()
{
    // 0 = E6B2/ABI → suportado (é o de hoje).
    {
        const auto s = selectEncoder(0);
        assert(s.kind == EncoderKind::Abi);
        assert(s.supported == true);
    }
    // 1 = MT6701 → reconhecido, mas NÃO suportado na v1 (→ M5).
    {
        const auto s = selectEncoder(1);
        assert(s.kind == EncoderKind::Mt6701);
        assert(s.supported == false);
    }
    // 2 = AS5047P → reconhecido, não suportado v1 (→ M5).
    {
        const auto s = selectEncoder(2);
        assert(s.kind == EncoderKind::As5047p);
        assert(s.supported == false);
    }
    // Valor inválido/desconhecido → fallback seguro pro ABI, marcado não-suportado.
    {
        const auto s = selectEncoder(99);
        assert(s.kind == EncoderKind::Abi);
        assert(s.supported == false);
    }
    std::printf("test_encoder_select: OK\n");
    return 0;
}
