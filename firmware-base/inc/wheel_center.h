// ============================================================================
//  DriveLab
//  wheel_center.h — O CENTRO do volante: um zero unico, compartilhado por todos
//  os consumidores de posicao (FFB, eixo do jogo, telemetria do app).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
//  POR QUE ESTE MODULO EXISTE (bancada 2026-08-07)
//
//  O encoder entrega uma contagem ACUMULADA: quantas voltas o eixo deu desde
//  que o firmware subiu. Isso NAO e "quanto o volante esta virado a partir do
//  centro" — sao coisas diferentes, e o firmware tratava as duas como a mesma.
//
//  Media na bancada, motor armado, ninguem tocando no aro:
//      pos  = 2,3 voltas   (o curso inteiro e +-1,5)
//      trq  = -10,0 Nm     CONSTANTE, o teto do modelo
//      Iq   = -18,2 A      CONSTANTE, com o volante PARADO
//  O batente virtual achava que o volante estava tres voltas alem do fim de
//  curso e saturava tentando traze-lo de volta — para sempre.
//
//  Os sintomas do dia inteiro sao todos esse mesmo comando errado:
//    · "o motor esquenta sem eu fazer nada"   -> 18 A parado e calor, nao defeito
//    · "empurro pra um lado, ele forca pro outro" -> batente saturado empurrando
//    · "se giro com a mao ele acelera"         -> girando PARA o centro, o motor
//                                                 ajuda com 10 Nm
//  A FOC estava correta o tempo todo: obedecia fielmente um comando errado.
//  Corrente alta PARECE falha de FOC, e foi por isso que a perseguimos por horas.
//
//  E havia uma segunda metade escondida: ffb_hid.cpp montava o eixo X do jogo
//  com a posicao CRUA e kSteerRangeTurns=1,5. A 2,3 voltas o eixo saturava em
//  32767 — o jogo via o volante TRAVADO no batente. Dois consumidores, dois
//  bugs, uma causa. Por isso o centro vira um modulo em vez de um static local:
//  tres lugares liam posicao e cada um tinha (ou nao tinha) o seu proprio zero.
//
//  CONSUMIDORES (todos devem usar wheel_center_pos_turns, nunca a contagem crua):
//    · ffb_task.cpp   — o torque de FFB, batente incluso
//    · ffb_hid.cpp    — o eixo X que o jogo le
//    · a0_channel.cpp — a telemetria de angulo do Studio
//
//  QUANDO O ZERO E ADOTADO: no PRIMEIRO arme depois do boot, e no ResetCenter
//  pedido pelo app. Nao a cada arme: um desarme transitorio (o churn ja medido)
//  re-centraria no meio de uma curva, movendo o batente sob as maos do piloto.
//  O centro fisico nao muda quando o motor pisca — o zero tambem nao deve.
//
//  LIMITE CONHECIDO: o zero nao sobrevive ao boot. Quem liga a base com o
//  volante torto corre com o batente torto ate reiniciar (ou clicar em
//  ResetCenter no app). Persistir o offset na flash exige encoder com indice ou
//  posicao absoluta, senao a contagem crua de um boot nao significa nada no
//  seguinte. Ver a memoria do encoder/indice antes de tentar.
//
#pragma once
#ifdef __cplusplus
extern "C" {
#endif

// Adota a posicao ATUAL do encoder como centro. Idempotente do ponto de vista
// do chamador: pode ser chamada quantas vezes quiser (ResetCenter faz isso).
void  wheel_center_capture(void);

// Posicao do volante RELATIVA ao centro, em voltas. E isto que todo consumidor
// deve usar. Antes de o centro ser adotado, devolve a contagem crua (offset 0).
float wheel_center_pos_turns(void);

/// Sentido do volante (setting 9). +1 = como esta ligado, -1 = invertido. Inverte o que o mundo VE
/// — forca, eixo do jogo e telemetria —, NUNCA a relacao encoder/motor da FOC (mexer nela com o
/// motor calibrado faz o torque sair no sentido errado). Ver o comentario longo no .cpp.
void wheel_center_set_direction(int dir);

// O zero em si (voltas de contagem crua) — diagnostico e telemetria.
float wheel_center_offset_turns(void);

// 0 ate o primeiro capture deste boot. O ffb_task usa para centrar so uma vez.
int   wheel_center_is_set(void);

#ifdef __cplusplus
}
#endif
