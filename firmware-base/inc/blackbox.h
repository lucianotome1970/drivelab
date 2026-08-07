// ============================================================================
//  DriveLab
//  blackbox.h — Caixa-preta de reset: por que o MCU reiniciou da última vez.
//
//  PARA QUE SERVE: em 2026-08-06 a base reiniciou sozinha na bancada e passamos
//  horas discutindo se era firmware, DRV travado ou fonte — sem nenhum dado que
//  separasse as hipóteses. "A placa reiniciou" precisa deixar de ser mistério.
//
//  DUAS FONTES, com alcances diferentes:
//
//  1) CAUSA DO RESET (RCC->CSR). O STM32 guarda, em flags do próprio RCC, o que
//     provocou o último reset: power-on, brown-out, pino NRST, software ou
//     watchdog. Lemos no boot e LIMPAMOS — assim a leitura seguinte se refere ao
//     reset seguinte, e não acumula histórico velho. Sobrevive inclusive a queda
//     de alimentação, que é justamente o caso que não conseguimos diagnosticar.
//
//  2) ÚLTIMO HARD FAULT (.noinit). PC/LR/CFSR do fault, gravados pelo handler.
//     Fica em .noinit, que o startup NÃO zera (ele só limpa .bss) — então sobrevive a
//     reset por software e por pino. ⚠️ NÃO sobrevive a queda de alimentação: é
//     RAM comum. Se `magic` não bater, não houve fault registrado (ou a energia
//     caiu no meio) e os campos não valem nada.
//
//  ⚠️ Nesta placa hard fault NÃO reinicia: o handler do ODrive trava num while(1)
//  e não há IWDG/WWDG de hardware armado. Ou seja, fault = base CONGELA. Se a
//  placa REINICIOU, a causa está na alimentação — e é (1) que vai dizer.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#ifndef DRIVELAB_BLACKBOX_H
#define DRIVELAB_BLACKBOX_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Causa do último reset, decodificada. Ordem = prioridade de diagnóstico: as flags do
// RCC podem vir combinadas (um power-on típico acende POR e PIN juntos), então
// reportamos a mais informativa.
enum {
    BB_RESET_DESCONHECIDA = 0,
    BB_RESET_POWER_ON     = 1,   // energia aplicada do zero — o normal ao ligar a fonte
    BB_RESET_BROWN_OUT    = 2,   // ⚠️ tensão caiu abaixo do limiar: fonte/carga, NÃO firmware
    BB_RESET_PINO_NRST    = 3,   // reset externo (inclui o do ST-Link ao gravar)
    BB_RESET_SOFTWARE     = 4,   // NVIC_SystemReset (nosso reboot / entrada em DFU)
    BB_RESET_IWDG         = 5,   // watchdog independente (não armado hoje)
    BB_RESET_WWDG         = 6,   // window watchdog (não armado hoje)
    BB_RESET_LOW_POWER    = 7,   // saída anormal de standby
};

#define BB_FAULT_MAGIC 0xDB1FA017u   // "DriveLab FAULT" — marca que os campos abaixo valem

typedef struct {
    uint32_t magic;   // BB_FAULT_MAGIC quando há um fault registrado
    uint32_t pc;      // endereço da instrução que faltou (casar com o .map/addr2line)
    uint32_t lr;      // quem chamou
    uint32_t cfsr;    // Configurable Fault Status Register — diz o TIPO do fault
    uint32_t count;   // faults desde o último power-on (>1 = está repetindo)
} BlackBoxFault;

// Legíveis por SWD sem halt (`mrw`), que é como diagnosticamos com o motor armado.
extern volatile uint32_t g_bb_reset_csr;      // cópia crua de RCC->CSR (bits 24-31)
extern volatile uint32_t g_bb_reset_reason;   // BB_RESET_* decodificado
extern BlackBoxFault     g_bb_fault;          // em .noinit (sobrevive a reset, não a power-off)

// Chamar UMA vez, cedo no boot, antes de qualquer coisa limpar as flags do RCC.
void blackbox_init(void);

// Chamado pelo handler de hard fault, imediatamente antes de ele travar.
void blackbox_record_fault(uint32_t pc, uint32_t lr, uint32_t cfsr);

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_BLACKBOX_H
