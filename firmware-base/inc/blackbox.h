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

// ⚠️ O magic MUDA quando o layout do struct muda (era ...A017). Ele nao valida so "houve fault":
// valida "estes bytes na .noinit sao do formato que este codigo espera". Sem trocar, um boot logo
// apos a gravacao leria o registro do firmware ANTIGO com o layout NOVO, e os campos novos sairiam
// de lixo — que e pior que nao ter registro, porque parece dado.
#define BB_FAULT_MAGIC 0xDB1FA018u   // "DriveLab FAULT" — marca que os campos abaixo valem

// COMO A BASE TRAVOU. Nem todo travamento e hard fault, e essa foi a lacuna que custou caro:
// o FreeRTOS trata estouro de pilha chamando um hook que termina em `for(;;)`, e o hook do ODrive
// faz exatamente isso. A CPU nao falta — ela fica presa num laco vazio. Sem fault, sem PC, sem
// nada: o watchdog reinicia 2 s depois e o boot seguinte so sabe dizer "reiniciou".
enum {
    BB_HANG_NENHUM         = 0,
    BB_HANG_HARD_FAULT     = 1,   // acesso invalido, instrucao ilegal — pc/lr/cfsr valem
    BB_HANG_STACK_OVERFLOW = 2,   // uma tarefa passou do fim da propria pilha; `task` diz qual
    BB_HANG_MALLOC_FALHOU  = 3,   // heap do FreeRTOS esgotado
};

typedef struct {
    uint32_t magic;   // BB_FAULT_MAGIC quando há um registro válido
    uint32_t kind;    // BB_HANG_* — sem isto, "sem fault registrado" e ambiguo
    uint32_t pc;      // endereço da instrução que faltou (casar com o .map/addr2line)
    uint32_t lr;      // quem chamou
    uint32_t cfsr;    // Configurable Fault Status Register — diz o TIPO do fault
    uint32_t task;    // 4 primeiros chars do nome da tarefa (estouro de pilha) — quem foi
    uint32_t count;   // ocorrências desde o último power-on (>1 = está repetindo)
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
// ⚠️ MUDA quando o layout do BlackBoxTrace muda (era ...7ACE). O magic nao valida so "ha rastro":
// valida "estes bytes sao do formato que este codigo espera". Sem trocar, o primeiro boot depois
// da gravacao leria o rastro do firmware ANTIGO com o layout NOVO — e os campos novos sairiam de
// lixo, que e pior que nao ter rastro porque parece dado.
// ⚠️ TROQUE ESTE VALOR SEMPRE QUE MEXER NO LAYOUT DE BlackBoxTrace. O magic não é enfeite: a
// struct vive em .noinit e SOBREVIVE ao reset, então um firmware novo lê a memória deixada pelo
// antigo. Se o layout mudou e o magic não, os campos são lidos deslocados e o rastro vira lixo com
// cara de medição — em 15/08/2026 acrescentei usb_claim_timeouts sem trocar o magic e o script
// reportou 184.581.233 desistências, número que só não enganou porque era absurdo.
// A cada mudança de layout: incremente o último dígito.
#define BB_TRACE_MAGIC 0x0B007AD1u   // "BOOT TRACE" rev 1

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
    // Sub-passos da TELEMETRIA. Em 14/08/2026 o rastro apontou "telemetria" e isso ainda eram duas
    // chamadas com o TinyUSB inteiro por baixo — e o TinyUSB toma o mutex do endpoint com
    // OSAL_TIMEOUT_WAIT_FOREVER, entao uma espera que nunca termina prende o laco de 1 kHz e o
    // watchdog reinicia a base 2 s depois. Estes tres separam ONDE, em vez de deixar adivinhar.
    BB_STEP_TLM_A0        = 9,   // a0_service (canal do app)
    BB_STEP_TLM_HID       = 10,  // hid_send_joystick, antes de qualquer chamada ao TinyUSB
    BB_STEP_TLM_HID_XFER  = 11,  // dentro do tud_hid_report — e aqui que o mutex e tomado
    // Sub-passos DENTRO do a0_service. Em 15/08/2026 os contadores do USB provaram que a pilha
    // estava VIVA (tarefa e interrupcao rodando aos milhares) enquanto o laco de FFB estava preso
    // aqui — ou seja, quem bloqueou foi so quem pediu, e nao a pilha inteira. Isso e a assinatura
    // de uma espera de mutex: o TinyUSB toma o do endpoint com OSAL_TIMEOUT_WAIT_FOREVER, e quem
    // fica esperando e a tarefa que chamou, sozinha.
    //
    // "Dentro do a0_service" ainda sao quatro chamadas. Estes passos dizem QUAL.
    BB_STEP_A0_READY      = 12,  // tud_hid_ready()
    BB_STEP_A0_LEITURA    = 13,  // tud_hid_report da resposta 0x16 (leitura de setting)
    BB_STEP_A0_MONTA      = 14,  // a0_build_state — nosso codigo, sem USB
    BB_STEP_A0_TELEMETRIA = 15,  // tud_hid_report da telemetria 0x21
};

typedef struct {
    uint32_t magic;      // BB_TRACE_MAGIC quando o conteúdo vale
    uint32_t step;       // BB_STEP_* em que o laço estava
    uint32_t tick;       // contador do laço — se estiver parado entre boots, travou de vez
    uint32_t last_step;  // o trecho ANTERIOR: um passo que trava muito cedo não chega a se marcar
    // AS CONDICOES DO INSTANTE. Saber ONDE travou nao basta para saber POR QUE: em 14/08/2026 a base
    // reiniciou durante uma batida, com o volante esterçado no fim do batente — corrente alta e
    // posicao no extremo ao mesmo tempo. Um pico de consumo afunda o barramento, e o USB engasgando
    // e o caminho conhecido para o STALL que leva ao travamento. Sem estes numeros, "foi a batida"
    // ou "foi coincidencia" continuam empatados.
    int32_t  vbus_mv;    // tensao do barramento
    int32_t  iq_ma;      // corrente do motor
    int32_t  pos_mrad;   // posicao do volante (extremo = perto do batente)
    // SINAIS DE VIDA DA PILHA USB. Moram AQUI, e nao em variaveis proprias, por um motivo que
    // custou uma ocorrencia inteira: variavel comum ZERA no boot. Eu os criei soltos, a base
    // reiniciou, e no boot seguinte estavam em zero — justamente a medicao que existia para decidir
    // a hipotese. Na .noinit eles sobrevivem ao reset, e blackbox_init fotografa o valor do boot
    // anterior antes de o novo comecar a contar.
    //
    // COMO LER, depois de um reinicio (ver os campos prev_ abaixo):
    //   irq subindo, task parado  -> travou na TAREFA (mutex/espera)
    //   os dois parados           -> travou na INTERRUPCAO, ou o host parou de falar
    //   os dois subindo           -> a pilha estava viva; o problema e outro
    uint32_t usb_task_ticks;  // voltas da tarefa do TinyUSB
    uint32_t usb_irq_ticks;   // entradas na interrupcao do USB
    // Quantas vezes o laco DESISTIU de tomar o mutex do endpoint em vez de esperar para sempre.
    // Zero e o esperado; qualquer valor acima disso e a prova de que a espera infinita acontecia —
    // e a diferenca entre perder um pacote de telemetria e perder a base inteira.
    uint32_t usb_claim_timeouts;
} BlackBoxTrace;

extern BlackBoxTrace g_bb_trace;

/// O rastro DO BOOT ANTERIOR, fotografado em blackbox_init() antes de o laço voltar a escrever.
/// Sem esta cópia o rastro seria inútil: o laço sobrescreve o valor em milissegundos, e quem for
/// ler por SWD depois do reset já encontra o do boot novo. Aqui vive o do que travou.
extern volatile uint32_t g_bb_trace_prev_step;
extern volatile uint32_t g_bb_trace_prev_last;
extern volatile uint32_t g_bb_trace_prev_tick;
extern volatile int32_t  g_bb_trace_prev_vbus_mv;
extern volatile int32_t  g_bb_trace_prev_iq_ma;
extern volatile int32_t  g_bb_trace_prev_pos_mrad;
extern volatile uint32_t g_bb_trace_prev_usb_task;
/// Desistencias de tomar o mutex do endpoint NO BOOT ANTERIOR. Sobrevive ao reset, que e
/// justamente o caso interessante: se a base reiniciou, este numero diz se ela chegou a
/// desistir antes — ou seja, se a espera infinita estava mesmo acontecendo.
extern volatile uint32_t g_bb_trace_prev_usb_claim;
extern volatile uint32_t g_bb_trace_prev_usb_irq;

/// Anota as condicoes eletricas/mecanicas do tick atual. Chamada UMA vez por volta do laco — tres
/// escritas em RAM, sem custo mensuravel a 1 kHz.
void blackbox_condicoes(int32_t vbus_mv, int32_t iq_ma, int32_t pos_mrad);

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

/// Chamado pelos hooks do FreeRTOS que terminam em `for(;;)`, antes de o laco prender.
/// `task` sao os 4 primeiros chars do nome da tarefa, ou 0 quando nao se aplica.
void blackbox_record_hang(uint32_t kind, uint32_t task);

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_BLACKBOX_H
