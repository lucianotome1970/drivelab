// ============================================================================
//  DriveLab
//  center_recovery.h — Recupera o CENTRO do volante no boot a partir de um
//  encoder absoluto de uma volta. Logica PURA: roda igual no firmware e num
//  alvo de teste no PC.
//
//  ── O PROBLEMA ─────────────────────────────────────────────────────────────
//  O wheel_center.h resolve o zero DENTRO de um boot, e o cabecalho dele
//  registra o que faltava: "o zero nao sobrevive ao boot. Quem liga a base com
//  o volante torto corre com o batente torto ate reiniciar."
//
//  Encoder incremental nao tem como resolver isso: a contagem crua de um boot
//  nao significa nada no seguinte. Um absoluto de uma volta resolve QUASE — e o
//  "quase" e o assunto deste arquivo.
//
//  ── POR QUE "QUASE" ────────────────────────────────────────────────────────
//  MT6701, MT6835 e AS5047P sao absolutos de UMA VOLTA. Eles respondem "em que
//  ponto do circulo voce esta", de 0 a 360°. Nao respondem "em qual volta".
//
//  O volante gira 900° de DOR — duas voltas e meia. Entao o sensor entrega a
//  posicao modulo uma volta, e falta saber a volta.
//
//  A saida: gravamos, junto com o centro, a posicao em que o volante ESTAVA ao
//  desligar. No boot, entre todas as voltas possiveis que produzem o angulo
//  lido, escolhemos a mais proxima daquela. Se ninguem girou mais de meia volta
//  com a base desligada, acertamos sempre.
//
//  ── POR QUE ISSO IMPORTA MAIS QUE CONVENIENCIA ─────────────────────────────
//  Errar a volta nao desalinha so o angulo na tela: joga o BATENTE uma volta
//  inteira para o lado errado. O volante giraria muito alem de onde o piloto
//  espera antes de encontrar o limite.
//
//  Por isso a funcao devolve `trusted`. Perto da fronteira de meia volta as
//  duas respostas sao igualmente plausiveis e nao ha como desempatar — ali a
//  resposta honesta e "nao sei", e quem chamou deve pedir recentralizacao em
//  vez de correr com um batente possivelmente torto.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_CENTER_RECOVERY_H
#define DRIVELAB_CENTER_RECOVERY_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Margem de duvida, em voltas. Acima disto a reconstrucao e ambigua demais para
// mover um batente: 0,35 volta = 126° de discrepancia entre onde o volante foi
// deixado e onde ele aparenta estar. O teto teorico e 0,5 (meia volta), onde as
// duas voltas vizinhas empatam; paramos antes para nao decidir no fio.
#define CENTER_RECOVERY_MAX_DRIFT_TURNS 0.35f

typedef struct {
    float   pos_turns;  // posicao do volante em voltas, a partir do centro gravado
    uint8_t trusted;    // 1 = pode adotar; 0 = ambigua, pedir recentralizacao
} CenterRecovery;

// cpr          contagens por volta do sensor absoluto (0 = sem sensor -> nao confiavel)
// center_count contagem absoluta que corresponde ao CENTRO (gravada na flash)
// last_turns   posicao em voltas na ultima vez que a base foi desligada
// now_count    contagem absoluta lida agora, no boot
static inline CenterRecovery center_recover(uint32_t cpr,
                                            uint32_t center_count,
                                            float    last_turns,
                                            uint32_t now_count) {
    CenterRecovery r;

    if (cpr == 0) {                 // sem sensor absoluto nao ha o que reconstruir
        r.pos_turns = 0.0f;
        r.trusted   = 0;
        return r;
    }

    // Parte fracionaria da posicao: onde estamos no circulo, relativo ao centro.
    // A conta e feita em inteiros e so entao vira fracao, para nao acumular erro.
    int32_t delta_counts = (int32_t)(now_count % cpr) - (int32_t)(center_count % cpr);
    if (delta_counts < 0) delta_counts += (int32_t)cpr;      // traz para [0, cpr)
    const float frac = (float)delta_counts / (float)cpr;      // [0, 1)

    // Entre todas as voltas possiveis (frac, frac±1, frac±2...), a mais proxima
    // de onde o volante foi deixado.
    const float alvo = last_turns - frac;
    const float k    = (alvo >= 0.0f) ? (float)(int32_t)(alvo + 0.5f)
                                      : (float)(int32_t)(alvo - 0.5f);
    r.pos_turns = frac + k;

    // Quanto o volante andou desde que desligamos. Por construcao isto nunca
    // passa de meia volta — o que medimos aqui e a CONFIANCA, nao o movimento
    // real: perto de meia volta, "andou +0,5" e "andou -0,5" sao a mesma leitura.
    float drift = r.pos_turns - last_turns;
    if (drift < 0.0f) drift = -drift;
    r.trusted = (drift <= CENTER_RECOVERY_MAX_DRIFT_TURNS) ? 1u : 0u;

    return r;
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_CENTER_RECOVERY_H
