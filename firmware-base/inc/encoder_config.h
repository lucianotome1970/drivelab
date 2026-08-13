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
//  ── POR QUE O MODELO PASSOU A IMPORTAR (2026-08-11) ────────────────────────
//  Antes este arquivo dizia que o modelo era assunto da UI e a placa so
//  precisava de interface e resolucao. Estava errado, e o erro era perigoso.
//
//  "Absoluto por SPI" nao e UM protocolo: cada chip fala o seu. A base sabe
//  conversar com a familia AMS (AS5047P) — inclusive com o tratamento do bit
//  EF, que fica preso e derruba toda leitura. Nao sabe conversar com MT6701
//  nem com MT6835: nao existe driver, nem aqui nem no ODrive.
//
//  Sem o modelo, escolher "MT6701 em SSI" gravava 16384 contagens por cima de
//  uma leitura A/B/Z — resolucao de um sensor aplicada na leitura de outro. E
//  o pior resultado possivel: PIOR do que ignorar a escolha, porque a base
//  continua girando e reportando um angulo errado por um fator de quatro.
//
//  Por isso `drivable`. Combinacao que a base nao sabe acionar NAO aplica
//  NADA — nem o modo, nem o CPR. Fica a configuracao do bring-up, que
//  funciona. Recusar inteiro e honesto; aplicar metade nao.
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

// Modo que a base aplica. Nao e um espelho do enum do ODrive: e o subconjunto
// que sabemos acionar. O bridge traduz para o valor do ODrive.
enum { ENC_MODE_INCREMENTAL = 0, ENC_MODE_SPI_ABS_AMS = 1 };

// Como o sensor foi ligado (espelha EncoderTech no app)
enum { ENC_IFACE_ABZ = 0, ENC_IFACE_SSI = 1, ENC_IFACE_SPI = 2 };

// Modelo do sensor (espelha EncoderModels no app)
enum {
    ENC_MODEL_GENERICO = 0,
    ENC_MODEL_E6B2     = 1,
    ENC_MODEL_MT6701   = 2,
    ENC_MODEL_MT6835   = 3,
    ENC_MODEL_AS5047P  = 4
};

typedef struct {
    uint8_t  mode;       // ENC_MODE_*
    uint32_t cpr;        // 0 = nao informado -> o chamador NAO aplica
    uint8_t  use_index;  // 1 = usar o pulso Z
    uint8_t  drivable;   // 0 = a base nao sabe acionar -> o chamador nao aplica NADA
} EncoderConfig;

static inline EncoderConfig encoder_config_from_settings(uint8_t model, uint8_t iface,
                                                         uint32_t resolution) {
    EncoderConfig c;
    c.cpr       = resolution;
    c.use_index = 0;   // o index continua desligado ate ser trabalho proprio

    if (iface == ENC_IFACE_ABZ) {
        // A/B/Z e o caminho validado, e TODOS os sensores do catalogo tem saida
        // A/B/Z. Independe do modelo: quadratura e quadratura.
        c.mode     = (uint8_t)ENC_MODE_INCREMENTAL;
        c.drivable = 1;
    } else if (iface == ENC_IFACE_SPI && model == ENC_MODEL_AS5047P) {
        // Unico absoluto que a base sabe ler hoje.
        c.mode     = (uint8_t)ENC_MODE_SPI_ABS_AMS;
        c.drivable = 1;
    } else {
        // MT6701 em SSI, MT6835 em SPI, e qualquer combinacao futura sem driver.
        // Sai com drivable=0 e o chamador mantem a configuracao do bring-up.
        c.mode     = (uint8_t)ENC_MODE_INCREMENTAL;
        c.drivable = 0;
    }
    return c;
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_ENCODER_CONFIG_H
