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
BlackBoxBoots g_bb_boots __attribute__((section(".noinit")));
BlackBoxTrace g_bb_trace __attribute__((section(".noinit")));

// Cópia do rastro do boot ANTERIOR. Em .bss comum de propósito: ela é preenchida no init a partir
// da .noinit e não precisa sobreviver a mais nada — o que precisa sobreviver é a origem.
volatile uint32_t g_bb_trace_prev_step = 0;
volatile uint32_t g_bb_trace_prev_last = 0;
volatile uint32_t g_bb_trace_prev_tick = 0;

// Marca o trecho do laço. Guarda o ANTERIOR junto: um trecho que trava logo na entrada pode não
// chegar a se marcar, e aí o par (anterior, atual) situa melhor que o atual sozinho.
void blackbox_step(uint32_t step) {
    if (g_bb_trace.magic != BB_TRACE_MAGIC) {   // primeira marcação depois de uma queda de energia
        g_bb_trace.magic     = BB_TRACE_MAGIC;
        g_bb_trace.tick      = 0;
        g_bb_trace.last_step = BB_STEP_NENHUM;
    }
    if (step != g_bb_trace.step) g_bb_trace.last_step = g_bb_trace.step;
    g_bb_trace.step = step;
    if (step == BB_STEP_INICIO) g_bb_trace.tick++;   // uma volta completa do laço
}

void blackbox_init(void) {
    const uint32_t csr = RCC->CSR;
    g_bb_reset_csr = csr;

    // CONTADOR DE BOOTS — ver o bloco em blackbox.h. Magic ausente = a energia caiu e a .noinit veio
    // com lixo: este é o primeiro boot do ciclo. Magic presente = a placa REINICIOU com a energia
    // mantida, e é esse o evento que se quer contar.
    if (g_bb_boots.magic != BB_BOOT_MAGIC) {
        g_bb_boots.magic = BB_BOOT_MAGIC;
        g_bb_boots.boots = 0;
        g_bb_boots.idx   = 0;
        for (uint32_t i = 0; i < BB_BOOT_HIST; ++i) g_bb_boots.csr[i] = 0;
    }
    // FOTOGRAFA O RASTRO ANTES QUE O LAÇO O APAGUE. Poucos milissegundos depois daqui o ffb_task
    // começa a marcar de novo, e o valor de quem travou some. Só vale se o magic bater: sem ele a
    // energia caiu e a .noinit veio com lixo.
    if (g_bb_trace.magic == BB_TRACE_MAGIC) {
        g_bb_trace_prev_step = g_bb_trace.step;
        g_bb_trace_prev_last = g_bb_trace.last_step;
        g_bb_trace_prev_tick = g_bb_trace.tick;
    }

    g_bb_boots.boots++;
    g_bb_boots.csr[g_bb_boots.idx % BB_BOOT_HIST] = csr;
    g_bb_boots.idx++;

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
