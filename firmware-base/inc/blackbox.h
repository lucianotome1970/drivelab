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
    BB_RESET_IWDG         = 5,   // watchdog independente — ARMADO desde 14/08/2026 (ver watchdog.h)
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

#define BB_BOOT_MAGIC  0x0B007C71u   // "BOOT CTR" — marca que o contador de boots vale

// ---------------------------------------------------------------------------------------------
// RASTRO DO LAÇO — onde o firmware estava quando o watchdog o reiniciou
// ---------------------------------------------------------------------------------------------
// POR QUE EXISTE: o watchdog salva a base, e ao salvá-la APAGA a evidência. Diferente do hard
// fault, ele não guarda PC nem LR — o reset é limpo, e no boot seguinte só se sabe QUE reiniciou,
// nunca ONDE. Em 14/08/2026 a base reiniciou três vezes num dia, com IWDGRSTF nos três boots e
// nenhuma pista do motivo.
//
// COMO FUNCIONA: o laço de 1 kHz marca em que trecho está antes de entrar nele. Como isto vive em
// .noinit, o valor SOBREVIVE ao reset — e no boot seguinte diz qual trecho estava executando
// quando o tempo acabou. Não é um depurador; é a diferença entre "travou em algum lugar" e "travou
// gravando a flash".
//
// ⚠️ Sobrevive a reset, NÃO a queda de energia (é RAM comum). Depois de tirar da tomada, o valor
// não vale nada — por isso o magic.
#define BB_TRACE_MAGIC 0x0B007ACEu   // "BOOT TRACE"

// Trechos do laço de FFB. Ordem = ordem de execução, para o número sozinho já situar.
enum {
    BB_STEP_NENHUM        = 0,
    BB_STEP_INICIO        = 1,   // topo do laço, antes de qualquer trabalho
    BB_STEP_TELEMETRIA    = 2,   // hid_send_joystick / a0_service (canal do app)
    BB_STEP_SAVE_FLASH    = 3,   // a0_commit_save — CONGELA a CPU de propósito
    BB_STEP_AUTOSCALE     = 4,   // dimensionamento dos limites de bus
    BB_STEP_GUARDAS       = 5,   // sobrevelocidade, ângulo elétrico, curso excedido
    BB_STEP_ARME          = 6,   // auto-arme / calibração pedida ao ODrive
    BB_STEP_TORQUE        = 7,   // cálculo do FFB e escrita do torque
    BB_STEP_FIM           = 8,   // depois do watchdog_feed, antes de dormir
};

typedef struct {
    uint32_t magic;      // BB_TRACE_MAGIC quando o conteúdo vale
    uint32_t step;       // BB_STEP_* em que o laço estava
    uint32_t tick;       // contador do laço — se estiver parado entre boots, travou de vez
    uint32_t last_step;  // o trecho ANTERIOR: um passo que trava muito cedo não chega a se marcar
} BlackBoxTrace;

extern BlackBoxTrace g_bb_trace;

/// O rastro DO BOOT ANTERIOR, fotografado em blackbox_init() antes de o laço voltar a escrever.
/// Sem esta cópia o rastro seria inútil: o laço sobrescreve o valor em milissegundos, e quem for
/// ler por SWD depois do reset já encontra o do boot novo. Aqui vive o do que travou.
extern volatile uint32_t g_bb_trace_prev_step;
extern volatile uint32_t g_bb_trace_prev_last;
extern volatile uint32_t g_bb_trace_prev_tick;

/// Marca o trecho atual. Chamada MUITAS vezes por volta do laço — é uma escrita em RAM, sem custo
/// mensurável a 1 kHz.
void blackbox_step(uint32_t step);

// ---------------------------------------------------------------------------------------------
// CONTADOR DE BOOTS — o que faltava para diagnosticar reinício INTERMITENTE.
//
// A caixa-preta guardava só o ÚLTIMO reset. Serve para "a placa reiniciou agora, por quê?", e não
// serve para "ela reinicia de vez em quando": quando alguém vai olhar, o registro já foi
// sobrescrito pelo boot seguinte, ou o problema não está acontecendo naquele instante.
//
// Isto conta os boots e guarda o CSR CRU de cada um. Fica em .noinit, e é justamente por isso que
// funciona como diagnóstico: sobrevive a reset e MORRE quando a energia cai. Então o contador em 7
// diz "a placa reiniciou 6 vezes desde que você ligou a fonte", que é exatamente a pergunta.
//
// Guarda o CSR e não a causa já decodificada porque a decodificação tem ambiguidade conhecida (as
// flags vêm combinadas) — com o valor cru dá para reinterpretar depois sem precisar reproduzir.
// ---------------------------------------------------------------------------------------------
#define BB_BOOT_HIST 8
typedef struct {
    uint32_t magic;                  // BB_BOOT_MAGIC quando o conteúdo vale
    uint32_t boots;                  // boots desde a última queda de energia (1 = só o power-on)
    uint32_t csr[BB_BOOT_HIST];      // CSR cru dos últimos boots, circular
    uint32_t idx;                    // onde entra o próximo
} BlackBoxBoots;

// Legíveis por SWD sem halt (`mrw`), que é como diagnosticamos com o motor armado.
extern volatile uint32_t g_bb_reset_csr;      // cópia crua de RCC->CSR (bits 24-31)
extern volatile uint32_t g_bb_reset_reason;   // BB_RESET_* decodificado
extern BlackBoxFault     g_bb_fault;          // em .noinit (sobrevive a reset, não a power-off)
extern BlackBoxBoots     g_bb_boots;          // idem — ver o bloco acima

// Chamar UMA vez, cedo no boot, antes de qualquer coisa limpar as flags do RCC.
void blackbox_init(void);

// Chamado pelo handler de hard fault, imediatamente antes de ele travar.
void blackbox_record_fault(uint32_t pc, uint32_t lr, uint32_t cfsr);

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_BLACKBOX_H
