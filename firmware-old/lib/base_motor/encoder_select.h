// ============================================================================
//  DriveLab Firmware
//  encoder_select.h — Seleção pura do tipo de encoder (BaseCfg.encoderType →
//  {kind, supported}). Sem Arduino/SimpleFOC → host-testável. v1: só ABI
//  (E6B2) é suportado; MT6701/AS5047P são reconhecidos mas ficam pro M5
//  (dependem do SPI3 compartilhado com o DRV8301 + pino CS + sensor físico).
//  Tipo não suportado/desconhecido ⇒ fallback seguro pro ABI (o chamador
//  mantém o Encoder incremental e loga um aviso — nunca brica o volante).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once
#include <cstdint>

enum class EncoderKind : uint8_t { Abi = 0, Mt6701 = 1, As5047p = 2 };

struct EncoderSelection
{
    EncoderKind kind;
    bool        supported;   // false ⇒ o chamador cai pro ABI e loga (v1)
};

// encoderType vem do BaseCfg (BID 18). v1: só 0 (ABI) é suportado.
inline EncoderSelection selectEncoder(uint8_t encoderType)
{
    switch (encoderType)
    {
        case 0:  return { EncoderKind::Abi,     true  };
        case 1:  return { EncoderKind::Mt6701,  false };
        case 2:  return { EncoderKind::As5047p, false };
        default: return { EncoderKind::Abi,     false };  // fallback seguro
    }
}
