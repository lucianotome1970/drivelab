// ============================================================================
//  DriveLab
//  ffb_curve_migrate.h — Traz a curva de forca de 5 pontos para a grade de 11.
//  Logica PURA (host-testada em test/test_ffb_curve_migrate.c).
//
//  POR QUE EXISTE: a curva tinha 5 pontos, de 25 em 25% (0/25/50/75/100). Passou
//  a ter 11, de 10 em 10%. Os cinco ids antigos (28-32) foram MANTIDOS para os
//  cinco primeiros pontos da grade nova — o que preserva os ids, mas NAO preserva
//  o significado: o valor gravado pensando em "50% da entrada" passou a ser lido
//  como "20% da entrada".
//
//  O efeito e uma curva espremida na metade de baixo, amplificando as forcas
//  PEQUENAS. Medido na bancada em 2026-08-12, numa placa com 0/22/36/47/52
//  salvos: em 10% de entrada saiam 22% em vez de 9%, e em 20% saiam 36% em vez
//  de 18% — 2,4x. Forca pequena e o que existe em linha reta, entao o sintoma
//  foi o volante TREMER na reta, que e dos mais dificeis de atribuir: parece que
//  "a base ficou nervosa", nao que uma tabela mudou de escala.
//
//  A migracao reinterpola: le a curva antiga como os 5 pontos que ela era e
//  amostra os 11 pontos novos em cima dela. Quem tinha uma curva ajustada
//  continua com a MESMA curva; so muda quantos pontos a descrevem.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_FFB_CURVE_MIGRATE_H
#define DRIVELAB_FFB_CURVE_MIGRATE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define FFB_CURVE_PONTOS_ANTIGOS 5
#define FFB_CURVE_PONTOS_NOVOS   11

// Reamostra `antiga` (5 pontos, de 25 em 25%) na grade de 11 pontos (de 10 em 10%).
// `nova` recebe os 11 valores. Interpolacao linear, que e a mesma que a curva de 5 pontos
// usava entre os seus pontos — logo o resultado descreve exatamente a mesma funcao.
static inline void ffb_curve_migrar_5_para_11(const int32_t* antiga, int32_t* nova) {
    for (int i = 0; i < FFB_CURVE_PONTOS_NOVOS; ++i) {
        const int x = i * 10;                       // 0,10,...,100 (% da entrada)
        const int seg = x / 25;                     // qual trecho da curva antiga
        if (seg >= FFB_CURVE_PONTOS_ANTIGOS - 1) {  // x = 100 cai exatamente no ultimo ponto
            nova[i] = antiga[FFB_CURVE_PONTOS_ANTIGOS - 1];
            continue;
        }
        const int x0 = seg * 25;
        const int32_t y0 = antiga[seg];
        const int32_t y1 = antiga[seg + 1];
        // +12 antes de dividir por 25 = arredondar ao inteiro mais proximo, em vez de truncar
        // (truncar puxaria a curva inteira para baixo, um pouco em cada ponto).
        nova[i] = y0 + ((y1 - y0) * (x - x0) + 12) / 25;
    }
}

// A curva salva precisa de migracao? Sim quando o blob e anterior aos pontos 5-10, que moram nos
// ids 49-54 — ou seja, quando ele tem menos campos do que o id do primeiro ponto novo.
// `campos_no_blob` e a contagem gravada no proprio blob.
//
// ⚠️ ZERO NAO E "BLOB ANTIGO", E "NAO HA BLOB". Flash apagada, placa de fabrica, primeiro uso: nao
// existe nada gravado, e os arrays ficam com os DEFAULTS — que ja nascem na grade nova de 11 pontos.
// Migra-los trata a curva linear como se fosse de 5 pontos e a ACHATA: a curva de fabrica
// 0,10,20...100 vira 0,4,8...40, e a base entrega no maximo 40% do que o jogo pede. Sem nada na tela
// sugerindo erro — a curva "existe" e parece intencional.
//
// Isso atingia TODO usuario novo, e era invisivel para nos: em qualquer placa nossa o blob ja
// existia, entao a condicao dava falso. Apareceu em 14/08/2026, no primeiro teste de instalacao numa
// placa apagada de proposito.
static inline int ffb_curve_precisa_migrar(uint16_t campos_no_blob, uint16_t id_primeiro_ponto_novo) {
    return campos_no_blob > 0 && campos_no_blob <= id_primeiro_ponto_novo;
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_FFB_CURVE_MIGRATE_H
