// ============================================================================
//  DriveLab
//  encoder_config.h — Traducao dos settings do encoder (modelo, tecnologia,
//  resolucao) para a configuracao aplicada no ODrive. Logica PURA: roda igual
//  no firmware e num alvo de teste no PC.
//
//  A GARANTIA: com os valores padrao (tudo zero) o resultado e a configuracao
//  de HOJE — modo incremental e CPR intocado. E o que protege a unica baseline
//  validada (4 voltas em 2026-08-06). O teste de host fixa isso, entao um
//  desvio aparece no PC e nao com o motor armado.
//
//  cpr == 0 significa "nao informado, nao aplicar". Nunca gravar zero: um CPR
//  zerado quebra a conversao de posicao (divisao por zero).
//
//  O MODELO nao e usado aqui: ele existe para a UI saber que tecnologias
//  oferecer e que resolucao preencher. A placa so precisa de interface e
//  resolucao.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_ENCODER_CONFIG_H
#define DRIVELAB_ENCODER_CONFIG_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

enum { ENC_MODE_INCREMENTAL = 0, ENC_MODE_SPI_ABS = 1 };
enum { ENC_IFACE_ABZ = 0, ENC_IFACE_SSI = 1, ENC_IFACE_SPI = 2 };

typedef struct {
    uint8_t  mode;       // ENC_MODE_*
    uint32_t cpr;        // 0 = nao informado -> o chamador NAO aplica
    uint8_t  use_index;  // 1 = usar o pulso Z
} EncoderConfig;

static inline EncoderConfig encoder_config_from_settings(uint8_t model, uint8_t iface,
                                                         uint32_t resolution) {
    (void)model;   // ver o cabecalho: o modelo e assunto da UI, nao da placa

    EncoderConfig c;
    c.mode      = (iface == ENC_IFACE_ABZ) ? (uint8_t)ENC_MODE_INCREMENTAL
                                           : (uint8_t)ENC_MODE_SPI_ABS;
    c.cpr       = resolution;
    c.use_index = 0;   // o index continua desligado ate ser trabalho proprio
    return c;
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_ENCODER_CONFIG_H
