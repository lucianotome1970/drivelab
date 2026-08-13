// ============================================================================
//  DriveLab
//  motor_config.h — Traducao dos settings de MOTOR (pares de polos, corrente de
//  calibracao) para a configuracao aplicada no ODrive. Logica PURA.
//
//  POR QUE EXISTE: os dois estavam CRAVADOS no firmware — pole_pairs=15 do
//  hoverboard e calibration_current=5A. Junto com o CPR, sao o que faz o motor
//  de OUTRA pessoa funcionar. Com eles fixos, o firmware era "o firmware do
//  nosso motor": quem usasse um motor com outro numero de imas teria corrente
//  entrando na bobina errada — calor sem torque.
//
//  A GARANTIA: com os valores padrao (pole_pairs=15, corrente=5) o resultado e
//  IDENTICO ao de hoje. E zero significa "nao informado, nao aplicar", nunca
//  "aplicar zero": zero pares de polos e zero corrente de calibracao sao
//  configuracoes sem sentido que quebrariam a partida.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_MOTOR_CONFIG_H
#define DRIVELAB_MOTOR_CONFIG_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    uint8_t pole_pairs;        // 0 = nao aplicar
    float   calib_current_a;   // 0 = nao aplicar (ja limitado ao current_lim)
    float   current_lim_a;     // 0 = nao aplicar (ja limitado pelo alcance do sensor)
    float   torque_constant;   // 0 = nao aplicar (nao medido, ou fora da faixa plausivel)
} MotorConfig;

// CONSTANTE DE TORQUE (Kt, Nm/A) — o cambio entre "quero X Nm" e "mando Y amperes".
//
// O firmware cravava 0,55 Nm/A, que NAO e medido: sai da formula generica de hoverboard (8,27/KV).
// O app ja tinha o campo, com o rotulo certo ("0 = nao medido"), e ele era ORFAO — o valor
// trafegava, era salvo, voltava na tela, e nada o lia.
//
// POR QUE IMPORTA MAIS QUE OS OUTROS: um Kt errado nao da erro nenhum. A conta fecha, a barra de
// torque do app mostra o valor pedido, o clipping conta certo — e o volante entrega outra coisa:
//
//     pedido 12 Nm -> 12 / 0,55 = 21,8 A -> se o Kt REAL for 0,40, chegam 8,7 Nm no volante
//
// A pessoa sente "fraco" e nao ha o que investigar, porque nada esta quebrado. Foi exatamente o
// quadro da bancada em 2026-08-11: 38% de clipping com sensacao de pouca forca.
//
// A DIRECAO DO ERRO NAO E SIMETRICA, e e por isso que existe faixa de sanidade:
//   - Kt informado MAIOR que o real  -> menos torque que o pedido. Frustra, nao machuca.
//   - Kt informado MENOR que o real  -> MAIS corrente que o necessario para o mesmo Nm. Um zero a
//     menos numa digitacao (0,05 no lugar de 0,50) faz o firmware pedir dez vezes a corrente, e o
//     calor cresce com o QUADRADO dela. Sem sensor de temperatura no motor, ninguem avisa.
//
// O current_lim continua sendo o teto duro; a faixa aqui e a rede que pega o erro de digitacao
// antes de ele virar corrente. Fora de [0,01; 5,0] Nm/A nao existe motor de volante: 0,01 seria um
// motor de drone minusculo e 5,0 um servo industrial de porte muito maior que qualquer base DD.
// Valor fora disso e engano, e engano se ignora — fica valendo o padrao do bring-up.
#define MOTOR_CONFIG_KT_MIN 0.01f
#define MOTOR_CONFIG_KT_MAX 5.00f

static inline float motor_config_sane_torque_constant(float kt_nm_per_a) {
    if (kt_nm_per_a < MOTOR_CONFIG_KT_MIN) return 0.0f;   // inclui 0 = "nao medido"
    if (kt_nm_per_a > MOTOR_CONFIG_KT_MAX) return 0.0f;   // digitacao absurda
    return kt_nm_per_a;
}

// LIMITE DE CORRENTE DO MOTOR — quanto o firmware deixa passar para as bobinas.
//
// Estava CRAVADO em 25 A, o valor do motor desta bancada, igual para qualquer um que gravasse o
// firmware. Mesmo problema que o CPR e os pares de polos tinham: descreve O MOTOR, entao tem que
// vir de quem tem o motor. Um motor menor queimaria antes de chegar aos 25 A; um maior ficaria
// entregando menos torque do que aguenta.
//
// TETO FISICO: o sensor de corrente da placa tem alcance finito (requested_current_range) e o
// ODrive exige folga (current_lim_margin) para nao saturar a leitura. Pedir corrente acima disso
// nao entrega mais torque — cega a medicao, e medicao cega num laco de corrente e o caminho para
// corrente no lugar errado. Por isso o teto e (alcance - margem), calculado pelo chamador.
static inline float motor_config_clamp_current_lim(float pedido_a, float teto_a) {
    if (pedido_a <= 0.0f) return 0.0f;                 // 0 = nao informado, chamador nao aplica
    if (teto_a > 0.0f && pedido_a > teto_a) return teto_a;
    return pedido_a;
}

// TETO DA CORRENTE DE CALIBRACAO — achado na bancada em 2026-08-10.
//
// Enquanto este setting era IGNORADO (corrente cravada em 5 A), um valor absurdo salvo na flash
// dormia sem efeito. Ao passar a ser aplicado, ele acordou: a placa da bancada tinha 30 A salvo de
// um teste antigo, com current_lim de 25 A — ou seja, a varredura de calibracao entraria ACIMA do
// limite de corrente do proprio motor. Para dimensionar: a receita validada deste projeto usa 3 a
// 5 A, e ha registro de 8 A travando o DRV8301.
//
// Calibrar acima do limite de corrente nao faz sentido em nenhum cenario: o limite existe
// justamente porque acima dele o estagio de potencia ou o motor sofrem. Entao o teto e o proprio
// current_lim. Vale para todo mundo, nao so para a nossa bancada — quem herdar um valor alto na
// flash (ou digitar errado) nao energiza o motor acima do que ele aguenta.
// `lim_pedido_a`  — o que o usuario escolheu no app (0 = nao informado)
// `lim_teto_a`    — teto fisico da placa: requested_current_range - current_lim_margin
// `lim_vigente_a` — o limite que vale HOJE, usado como teto da calibracao quando o usuario nao
//                   informou um novo (senao a primeira gravacao calibraria sem teto nenhum)
// `kt_pedido`     — Kt medido pelo usuario, em Nm/A (0 = nao medido; ver a nota longa acima)
static inline MotorConfig motor_config_from_settings(uint8_t pole_pairs,
                                                     uint8_t calib_current_a,
                                                     float   lim_pedido_a,
                                                     float   lim_teto_a,
                                                     float   lim_vigente_a,
                                                     float   kt_pedido) {
    MotorConfig c;
    c.pole_pairs      = pole_pairs;
    c.current_lim_a   = motor_config_clamp_current_lim(lim_pedido_a, lim_teto_a);
    c.torque_constant = motor_config_sane_torque_constant(kt_pedido);

    // O teto da calibracao e o limite que VAI VALER — o novo, se informado; senao o de hoje.
    // A ordem importa: baixar o limite do motor precisa arrastar a corrente de calibracao junto,
    // ou a calibracao energizaria acima do limite recem-escolhido.
    const float lim_efetivo = (c.current_lim_a > 0.0f) ? c.current_lim_a : lim_vigente_a;
    c.calib_current_a = (float)calib_current_a;
    if (lim_efetivo > 0.0f && c.calib_current_a > lim_efetivo)
        c.calib_current_a = lim_efetivo;
    return c;
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_MOTOR_CONFIG_H
