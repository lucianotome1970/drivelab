// ============================================================================
//  DriveLab
//  blackbox.cpp — Caixa-preta de reset. Ver blackbox.h para o porquê.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include "blackbox.h"
#include "stm32f4xx_hal.h"

volatile uint32_t g_bb_reset_csr    = 0;
volatile uint32_t g_bb_reset_reason = BB_RESET_DESCONHECIDA;

// .noinit: o startup zera só de _sbss a _ebss, e esta seção fica fora desse intervalo — por isso o
// conteúdo sobrevive a um reset que não corte a energia. É o único jeito de um fault contar o que
// aconteceu depois de a placa já ter reiniciado.
BlackBoxFault g_bb_fault __attribute__((section(".noinit")));

void blackbox_init(void) {
    const uint32_t csr = RCC->CSR;
    g_bb_reset_csr = csr;

    // Ordem = do mais informativo para o menos. As flags vêm COMBINADAS (um power-on normal acende
    // POR e PIN juntos), então testar na ordem errada reportaria sempre "pino" e escondia o
    // brown-out — que é exatamente o caso que queremos pegar.
    if      (csr & RCC_CSR_LPWRRSTF) g_bb_reset_reason = BB_RESET_LOW_POWER;
    else if (csr & RCC_CSR_WWDGRSTF) g_bb_reset_reason = BB_RESET_WWDG;
    else if (csr & RCC_CSR_IWDGRSTF) g_bb_reset_reason = BB_RESET_IWDG;
    else if (csr & RCC_CSR_SFTRSTF)  g_bb_reset_reason = BB_RESET_SOFTWARE;
    else if (csr & RCC_CSR_BORRSTF)  g_bb_reset_reason = BB_RESET_BROWN_OUT;
    else if (csr & RCC_CSR_PORRSTF)  g_bb_reset_reason = BB_RESET_POWER_ON;
    else if (csr & RCC_CSR_PINRSTF)  g_bb_reset_reason = BB_RESET_PINO_NRST;
    else                             g_bb_reset_reason = BB_RESET_DESCONHECIDA;

    // Limpa as flags: sem isto elas se acumulam e a leitura passa a descrever o histórico inteiro
    // desde a última vez que alguém limpou, e não o reset que acabou de acontecer.
    __HAL_RCC_CLEAR_RESET_FLAGS();

    // Se a energia caiu, a CCM veio com lixo — zerar para não ler um "fault" que nunca existiu.
    // Num reset com energia mantida o magic sobrevive e o registro do fault é preservado.
    if (g_bb_fault.magic != BB_FAULT_MAGIC) {
        g_bb_fault.magic = 0;
        g_bb_fault.pc = g_bb_fault.lr = g_bb_fault.cfsr = g_bb_fault.count = 0;
    }
}

void blackbox_record_fault(uint32_t pc, uint32_t lr, uint32_t cfsr) {
    // Preserva o PRIMEIRO fault: o primeiro é o diagnóstico, os seguintes costumam ser consequência
    // (o handler já desligou o PWM, o estado está inconsistente). Só o contador segue subindo.
    if (g_bb_fault.magic != BB_FAULT_MAGIC) {
        g_bb_fault.magic = BB_FAULT_MAGIC;
        g_bb_fault.pc    = pc;
        g_bb_fault.lr    = lr;
        g_bb_fault.cfsr  = cfsr;
        g_bb_fault.count = 0;
    }
    g_bb_fault.count++;
}
