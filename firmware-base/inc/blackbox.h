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
#define BB_TRACE_MAGIC 0x0B007AD3u   // "BOOT TRACE" rev 3 (entrou txfe_desistencias)

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
    // Sub-passos DENTRO do tud_hid_report. Em 17/08/2026, com a placa ZERADA (sem configuracao que
    // pudesse estar corrompida), o rastro seguia parando no passo 15 — dentro desta unica chamada —
    // com a pilha USB viva e o mutex do endpoint sem estourar prazo. Ou seja: os dois suspeitos que
    // ja tratamos estao descartados por medicao, e "dentro do TinyUSB" ainda sao quatro etapas.
    //
    // Estes quatro dizem QUAL delas. Sem eles a proxima queda devolve a mesma informacao das
    // anteriores, e o diagnostico anda em circulo — foi o que aconteceu tres noites seguidas.
    BB_STEP_USB_CLAIM     = 16,  // pedindo o endpoint (mutex, prazo de 2 ms)
    BB_STEP_USB_COPIA     = 17,  // endpoint na mao, copiando o payload
    BB_STEP_USB_XFER      = 18,  // entregando ao driver (usbd_edpt_xfer)
    BB_STEP_USB_FIFO      = 19,  // dentro do DWC2, escrevendo no FIFO de transmissao
    // O 19 apontou certo em 17/08/2026 — e ainda cobria muita coisa: a escrita, o retorno dela, a
    // saida da secao critica e o caminho de volta ate o proximo marco. Estes dois cortam esse
    // trecho ao meio, que e como se acha um travamento sem chutar: bissecao, nao palpite.
    BB_STEP_USB_FIFO_FIM  = 20,  // a escrita no FIFO RETORNOU
    BB_STEP_USB_XFER_FIM  = 21,  // dcd_edpt_xfer retornou (secao critica ja liberada)
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
    // Quantas vezes uma espera por bits do DWC2 estourou o teto que pusemos nela (ver o patch em
    // dcd_dwc2.c). Morava numa variavel comum e ZERAVA no boot — inutil, porque a pergunta so
    // aparece depois do reinicio. Mesmo erro que ja tinhamos cometido com os sinais de vida da USB.
    uint32_t dwc2_wait_timeouts;
    // Quantas vezes a interrupcao de "FIFO vazio" foi DESLIGADA por falta de progresso. Zero e o
    // esperado. Acima disso, o host parou de drenar o endpoint e nos soltamos em vez de deixar a
    // interrupcao disparar sem fim — que era o que reiniciava a base. Ver o patch em dcd_dwc2.c.
    uint32_t txfe_desistencias;
    // Quantas vezes DEVOLVER o endpoint nao conseguiu o mutex no prazo. Este contador nasceu de uma
    // assimetria que passou despercebida: quando pusemos prazo em TOMAR o endpoint, deixamos
    // DEVOLVER esperando para sempre — e devolver roda dentro da tarefa da pilha USB. Bastava o
    // mutex estar ocupado no instante errado para a tarefa parar de vez, e uma base que nao responde
    // trava o cadastro de dispositivos do PC inteiro (o painel de controles de jogo deixa de abrir
    // ate a base ser desligada). Zero e o esperado.
    uint32_t usb_release_timeouts;
    // ONDE a tarefa da pilha USB estava quando paramos de ve-la andar. usb_task_ticks ja dizia QUE
    // ela parou; sem este campo, nao dizia ONDE — e a pilha tem meia duzia de lugares onde esperar.
    // Ver BB_USBT_* logo abaixo. Fica zerado enquanto a tarefa esta fora, entre uma volta e outra.
    uint32_t usb_task_step;
    // ============================================================================================
    // O FILME: os ultimos acontecimentos do USB antes do silencio
    // ============================================================================================
    // Contadores dizem QUE parou. Nao dizem o que aconteceu ANTES — e e o antes que explica por que
    // o PC fecha a porta. Cada acontecimento do USB (reset de barramento, suspensao, retomada,
    // desconexao, pergunta do PC, transferencia concluida) passa por UM unico ponto do codigo, e e
    // ali que anotamos. Vinte e quatro posicoes em circulo bastam: o que interessa sao os ultimos
    // milissegundos, e o rastro sobrevive ao reset.
    //
    // Formato de cada anotacao: milissegundos nos 24 bits de cima, codigo do acontecimento nos 8 de
    // baixo (BB_UEV_* abaixo). Com os milissegundos da para ver o RITMO — dez resets de barramento
    // em cem milissegundos contam uma historia bem diferente de um reset a cada dois segundos.
    // Transferencias concluidas. Nao entram no filme (mil por segundo o encheriam em 24 ms); aqui
    // servem para responder "o fluxo estava vivo?" sem gastar o espaco do que e raro.
    uint32_t usb_entregas;
    // Relogio do sistema operacional na ultima volta do laco. E o que separa duas causas opostas
    // de travamento: se ele ANDOU enquanto o laco nao rodava, alguem de prioridade maior
    // monopolizou o processador; se ele PAROU, o proprio sistema travou (interrupcao presa). Sem
    // isto, "travou no fim do laco" nao diz qual das duas — e as correcoes sao opostas.
    uint32_t relogio_so;
    uint32_t usb_filme[24];
    uint32_t usb_filme_pos;
} BlackBoxTrace;

/// Acontecimentos anotados no filme (byte de baixo de cada posicao de usb_filme).
enum {
    BB_UEV_RESET     = 1,   // o PC reiniciou o barramento (recomecar do zero)
    BB_UEV_DESLIGOU  = 2,   // o PC nos tirou do barramento
    BB_UEV_DORMIU    = 3,   // suspensao
    BB_UEV_ACORDOU   = 4,   // retomada
    BB_UEV_PERGUNTA  = 5,   // o PC perguntou algo (transferencia de controle)
    BB_UEV_SOF       = 7,   // marco de tempo
    BB_UEV_RECUSA    = 8,   // NAO soubemos responder: canal de controle travado
};

/// Anota um acontecimento no filme. Chamavel da interrupcao: so escreve duas palavras.
void blackbox_usb_evento(uint8_t codigo);

/// Marcos DENTRO da tarefa da pilha USB (g_bb_trace.usb_task_step).
enum {
    BB_USBT_FORA      = 0,   // entre uma volta e outra: normal
    BB_USBT_FILA      = 1,   // pegando o proximo evento da fila
    BB_USBT_RESET     = 2,   // tratando reset de barramento
    BB_USBT_SETUP     = 3,   // respondendo a uma pergunta do PC (descritor, configuracao...)
    BB_USBT_XFER      = 4,   // avisando a classe que uma transferencia terminou
    BB_USBT_SUSPEND   = 5,   // suspensao / retomada / desconexao
    BB_USBT_CLAIM     = 10,  // tomando o endpoint (mutex)
    BB_USBT_RELEASE   = 11,  // devolvendo o endpoint (mutex)
};

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
/// Relogio do sistema na ultima volta antes do travamento — ver relogio_so.
extern volatile uint32_t g_bb_trace_prev_relogio;
/// Desistencias de tomar o mutex do endpoint NO BOOT ANTERIOR. Sobrevive ao reset, que e
/// justamente o caso interessante: se a base reiniciou, este numero diz se ela chegou a
/// desistir antes — ou seja, se a espera infinita estava mesmo acontecendo.
extern volatile uint32_t g_bb_trace_prev_usb_claim;
/// Esperas do DWC2 estouradas NO BOOT ANTERIOR — a prova que so existe depois do reset.
extern volatile uint32_t g_bb_trace_prev_dwc2_wait;
/// Desistencias da interrupcao TXFE NO BOOT ANTERIOR — sobrevive ao reset, que e quando interessa.
extern volatile uint32_t g_bb_trace_prev_txfe;
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
