// ============================================================================
//  DriveLab
//  watchdog.h — Watchdog de hardware (IWDG): a base REINICIA em vez de congelar.
//
//  POR QUE EXISTE: ate 14/08/2026 um travamento no firmware significava base morta
//  ate alguem tirar da tomada. Aconteceu com o driver USB (tempestade de
//  interrupcao no TXFE, ver o patch em dcd_dwc2.c) e com o breakpoint do TinyUSB.
//  Os dois foram corrigidos — mas "corrigimos os que achamos" nao e o mesmo que
//  "nao ha mais nenhum", e um volante que morre no meio de uma sessao e um volante
//  que nao se entrega a ninguem.
//
//  O QUE MUDA:
//      antes:  trava -> congela para sempre -> so a tomada resolve
//      agora:  trava -> reinicia em ~2 s   -> volta sozinha
//
//  O reset tambem DESARMA o motor, o que e mais seguro do que congelar com
//  corrente aplicada — foi assim que a base ja ficou aquecendo parada.
//
//  E deixa RASTRO: o reset por watchdog acende IWDGRSTF no RCC->CSR, que a
//  caixa-preta le e decodifica no boot (ver blackbox.h). Deixa de ser "sumiu do
//  nada" e passa a ser um numero que da para acompanhar entre sessoes.
//
//  ⚠️ NAO substitui corrigir a causa. Watchdog e a rede, nao o chao.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_WATCHDOG_H
#define DRIVELAB_WATCHDOG_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Liga o IWDG. Depois de ligado NAO ha como desligar por software (so reset), que
// e justamente o ponto: ninguem desativa a rede por engano.
//
// Chamar DEPOIS da inicializacao pesada do boot e ANTES do laco de FFB comecar.
void watchdog_init(void);

// Alimenta. Precisa ser chamado de um lugar que SO roda com o sistema saudavel —
// ver a nota no .cpp sobre por que e o laco de 1 kHz, e nao um timer.
void watchdog_feed(void);

// Quantas vezes esta placa reiniciou por watchdog (lido do CSR pela caixa-preta).
// 0 = nunca. Cresce = ha travamento acontecendo em campo, mesmo que o usuario nao
// perceba mais, porque a base volta sozinha.
uint32_t watchdog_reset_count(void);

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_WATCHDOG_H
