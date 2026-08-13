// ============================================================================
//  DriveLab
//  thermal_config.h — Traducao do setting de temperatura dos FETs para os dois
//  limites que o ODrive usa. Logica PURA: roda igual no firmware e num alvo de
//  teste no PC.
//
//  ── O QUE JA EXISTIA ───────────────────────────────────────────────────────
//  A protecao termica dos FETs NAO e nova: o ODrive tem um limitador que le o
//  termistor da placa e, entre um limite inferior e um superior, vai CORTANDO a
//  corrente proporcionalmente — no superior ela chega a zero, e 5 °C acima disso
//  o eixo desarma. Ela ja roda hoje, com 100 °C e 120 °C de fabrica.
//
//  O que faltava era o nosso setting chegar la. O campo existia no app, era
//  salvo na placa, voltava certo ao reiniciar — e ninguem o lia.
//
//  ── POR QUE O SETTING E O LIMITE DE BAIXO, E NAO O DE CIMA ─────────────────
//  O campo se chama "corte de temperatura dos FETs" e o texto de ajuda diz
//  "temperatura em que a placa REDUZ ou corta". Mapeamos para onde a reducao
//  COMECA, com o corte total 20 °C acima (a mesma banda do ODrive).
//
//  O contrario seria pior de um jeito que ja nos custou caro: com o padrao de
//  85 °C tratado como corte total, a reducao comecaria em 65 °C — perto demais
//  dos ~43 °C que esta placa marca em repouso. A base perderia forca em sessao
//  longa sem nada na tela explicando, e "a base esta fraca" e exatamente o tipo
//  de sintoma que passamos um dia inteiro caçando.
//
//  Com o padrao: reduz a partir de 85, zera em 105, desarma em 110.
//
//  Zero significa "nao informado, nao aplicar" — nunca gravar zero, que
//  desligaria a protecao inteira sem ninguem pedir.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_THERMAL_CONFIG_H
#define DRIVELAB_THERMAL_CONFIG_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Largura da faixa de reducao, em °C. E a do proprio ODrive (100 -> 120), e o
// que da tempo de a temperatura estabilizar antes do corte total.
#define THERMAL_DERATE_BAND_C 20.0f

typedef struct {
    float   lower;  // °C — abaixo disto, corrente cheia; daqui pra cima vai reduzindo
    float   upper;  // °C — aqui a corrente chega a zero (e +5 desarma o eixo)
    uint8_t apply;  // 0 = nao informado, o chamador NAO mexe na configuracao
} ThermalConfig;

static inline ThermalConfig thermal_config_from_settings(uint8_t fet_limit_c) {
    ThermalConfig t;

    if (fet_limit_c == 0) {          // nao informado: mantem o que o ODrive tem
        t.lower = 0.0f;
        t.upper = 0.0f;
        t.apply = 0;
        return t;
    }

    t.lower = (float)fet_limit_c;
    t.upper = (float)fet_limit_c + THERMAL_DERATE_BAND_C;
    t.apply = 1;
    return t;
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_THERMAL_CONFIG_H
