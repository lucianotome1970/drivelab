// ============================================================================
//  DriveLab
//  osc_guard.h — Guarda de oscilacao: amortece SO quando o volante entra em
//  auto-oscilacao, e sai de cena no resto do tempo. Logica PURA
//  (host-testada em test/test_osc_guard.c).
//
//  O PROBLEMA. Levantar o comeco da curva de forca aumenta o ganho perto do
//  zero, e ganho alto em torno do ponto de equilibrio faz o laco
//  volante-piloto-base se auto-alimentar:
//
//      micro-movimento -> curva amplifica -> forca maior -> mais movimento -> ...
//
//  Na bancada (2026-08-12): com a curva no padrao (reta) nao treme; ao levantar
//  o comeco, treme na reta. Nao e ruido do jogo — e oscilacao de malha fechada.
//
//  POR QUE NAO O DAMPER. O damper resolve, e cobra caro: ele e SEMPRE ativo e
//  produz forca proporcional a velocidade — exatamente o que a textura de pista
//  e (movimento rapido de pequena amplitude). Damper alto deixa o volante firme
//  e MORTO. O usuario perguntou isso antes de aceitar a solucao, e tinha razao.
//
//  O QUE ESTA GUARDA FAZ DE DIFERENTE: ela reconhece a ASSINATURA da oscilacao
//  — reversoes de sentido em intervalo curto e REGULAR — e so entao amortece.
//  Textura de pista nao inverte o sentido do volante ritmadamente; oscilacao
//  sim. Fora do episodio, o ganho da guarda e zero e nada e tirado do piloto.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_OSC_GUARD_H
#define DRIVELAB_OSC_GUARD_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Velocidade minima (rad/s) para uma reversao CONTAR. Perto de zero o sinal da velocidade troca por
// ruido de quantizacao do encoder, e sem este piso a guarda dispararia com o volante parado.
#define OSC_GUARD_VEL_MIN_RADPS   0.35f
// Intervalo maximo entre reversoes para elas serem consideradas do MESMO episodio. 120 ms = ~4 Hz,
// o piso da faixa em que um volante DD oscila. Reversao mais espacada que isso e o piloto dirigindo.
#define OSC_GUARD_INTERVALO_MS    120
// Quantas reversoes rapidas seguidas antes de agir. Duas seriam uma correcao brusca de trajetoria;
// quatro ja e ritmo, e ritmo e o que distingue oscilacao de manobra.
#define OSC_GUARD_REVERSOES       4
// Subida rapida (o episodio precisa ser cortado na hora) e descida lenta (sair de repente devolveria
// o solavanco que a guarda acabou de evitar). Em fracao por ms.
#define OSC_GUARD_ATAQUE_POR_MS   0.05f
#define OSC_GUARD_DECAIMENTO_POR_MS 0.004f
// Amortecimento no nivel maximo, em Nm por rad/s. Mesma ordem do damper que o usuario validou em
// 2026-08-08 (88% = 0,264), porque e a intensidade que comprovadamente segura este conjunto.
#define OSC_GUARD_DAMPING_MAX     0.30f

typedef struct {
    int8_t   sinal_ant;        // sentido da velocidade no tick anterior (-1, 0, +1)
    uint16_t ms_desde_reversao; // satura; so precisamos saber se passou do intervalo
    uint8_t  reversoes;        // reversoes rapidas consecutivas
    float    nivel;            // 0..1 — quanto da guarda esta atuando agora
} OscGuard;

static inline void osc_guard_reset(OscGuard* g) {
    g->sinal_ant = 0; g->ms_desde_reversao = 0; g->reversoes = 0; g->nivel = 0.0f;
}

/// Avanca a guarda um tick e devolve o TORQUE de amortecimento a somar (Nm). `vel_radps` e a
/// velocidade do volante; `dt_ms` o passo do laco (1 no nosso caso).
/// Devolve 0 enquanto nao houver oscilacao — e esse zero e a razao de ser desta guarda.
static inline float osc_guard_update(OscGuard* g, float vel_radps, uint16_t dt_ms, int habilitada) {
    if (!habilitada) { osc_guard_reset(g); return 0.0f; }

    if (g->ms_desde_reversao < 60000) g->ms_desde_reversao += dt_ms;

    const int8_t sinal = (vel_radps >  OSC_GUARD_VEL_MIN_RADPS) ?  1
                       : (vel_radps < -OSC_GUARD_VEL_MIN_RADPS) ? -1 : 0;

    if (sinal != 0) {
        if (g->sinal_ant != 0 && sinal != g->sinal_ant) {
            // Reversao. Rapida o bastante para ser do mesmo episodio?
            if (g->ms_desde_reversao <= OSC_GUARD_INTERVALO_MS) {
                if (g->reversoes < 255) g->reversoes++;
            } else {
                g->reversoes = 1;   // recomeca a contagem: foi manobra, nao ritmo
            }
            g->ms_desde_reversao = 0;
        }
        g->sinal_ant = sinal;
    }

    // Episodio esfriou (o volante parou de inverter): esquece o ritmo acumulado. Sem isto, quatro
    // reversoes espalhadas ao longo de uma volta inteira acabariam somando e a guarda entraria
    // numa curva longa, onde nao ha oscilacao nenhuma.
    if (g->ms_desde_reversao > OSC_GUARD_INTERVALO_MS * 2) g->reversoes = 0;

    const int oscilando = (g->reversoes >= OSC_GUARD_REVERSOES);
    if (oscilando) {
        g->nivel += OSC_GUARD_ATAQUE_POR_MS * (float)dt_ms;
        if (g->nivel > 1.0f) g->nivel = 1.0f;
    } else {
        g->nivel -= OSC_GUARD_DECAIMENTO_POR_MS * (float)dt_ms;
        if (g->nivel < 0.0f) g->nivel = 0.0f;
    }

    return -g->nivel * OSC_GUARD_DAMPING_MAX * vel_radps;
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_OSC_GUARD_H
