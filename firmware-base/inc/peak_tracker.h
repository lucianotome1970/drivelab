// ============================================================================
//  DriveLab
//  peak_tracker.h — Rastreador dos picos POSITIVO e NEGATIVO de um sinal
//  bidirecional. Logica PURA (sem STM32, sem periferico): roda igual no
//  firmware e num alvo de teste no PC.
//
//  POR QUE DOIS PICOS E NAO O MODULO: a corrente de FFB e bidirecional. Com um
//  pico so voce sabe QUANTA forca houve; com os dois voce sabe se ela e
//  SIMETRICA. Pico positivo muito maior que o negativo aponta referencia de
//  posicao deslocada — o sintoma "forca so de um lado". A tela passa a dar
//  diagnostico, nao so informacao.
//
//  POR QUE NAO A MEDIA: a media de um sinal bidirecional se cancela perto de
//  zero. E o que o monitor mostra hoje, e por isso a leitura de corrente nao
//  informa nada.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_PEAK_TRACKER_H
#define DRIVELAB_PEAK_TRACKER_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    int16_t pos;   // maior valor positivo visto (0 se nunca houve)
    int16_t neg;   // menor valor negativo visto (0 se nunca houve)
} PeakTracker;

static inline void peak_tracker_init(PeakTracker* p) {
    p->pos = 0;
    p->neg = 0;
}

// Chamada a cada ciclo do laco de 1 kHz. So compara e guarda — nao filtra,
// nao decai, nao toca em periferico.
static inline void peak_tracker_update(PeakTracker* p, int32_t v) {
    if (v > 32767)  v =  32767;    // satura em vez de estourar o int16
    if (v < -32768) v = -32768;

    if (v > (int32_t)p->pos) p->pos = (int16_t)v;
    if (v < (int32_t)p->neg) p->neg = (int16_t)v;
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_PEAK_TRACKER_H
