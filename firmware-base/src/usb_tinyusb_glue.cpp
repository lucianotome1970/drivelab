// firmware-base — cola TinyUSB ↔ interface USB do ODrive (STAGE 1: CDC).
//
// Estratégia (a mesma que projetos ODrive-base usam): manter o interface_usb.cpp
// do ODrive INTACTO e implementar, via TinyUSB, as funções que ele chama:
//   - CDC_Transmit_FS()        : ODrive escreve → CDC IN do TinyUSB
//   - USBD_CDC_ReceivePacket() : ODrive arma um buffer de RX → drenamos o FIFO do TinyUSB nele
//   - MX_USB_DEVICE_Init()     : setup do PHY OTG_FS + tusb_init + task
// Os eventos do ODrive (osMessagePut: 1=conecta, 2=desconecta, 3=CDC TX done) ficam iguais.
//
// Escrito a partir das APIs (TinyUSB=MIT, interface do ODrive=MIT), entendendo cada peça —
// não é cópia de glue GPL de terceiros. O detalhe crítico do MKS (NOVBUSSENS, porque a
// VBUS não está ligada no PA9) está incorporado.
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#include <stdint.h>
#include <string.h>
#include <cmsis_os.h>
#include "stm32f4xx_hal.h"
#include "tusb.h"
#include "blackbox.h"   // contadores em .noinit (sobrevivem ao reset)
extern "C" {
#include "usbd_def.h"
#include "usbd_cdc.h"
}

// Monta o report descriptor combinado (ffb+a0) numa fonte única de tamanho — usb_descriptors.c.
extern "C" void usb_hid_report_desc_build(void);

// Globais do lado do ODrive (interface_usb.cpp / board.cpp / main.cpp)
extern "C" USBD_HandleTypeDef usb_dev_handle;                 // dummy em board_v3.cpp
extern "C" osMessageQId usb_event_queue;                      // fila de eventos (main.cpp)
extern "C" void usb_rx_process_packet(uint8_t *buf, uint32_t len, uint8_t endpoint_pair); // dispatch RX

// Modelo de RX do ODrive: ele "arma" um buffer; nós copiamos do FIFO do TinyUSB quando chega.
static struct { uint8_t* buf; uint16_t size; uint8_t ep; volatile bool active; } s_rx{};

// ============================================================================================
// SINAIS DE VIDA DA PILHA USB — SÓ OBSERVAM, não mudam nada.
//
// POR QUE EXISTEM: em 15/08/2026 a base ficou com o USB morto e o resto perfeito — laço de FFB a
// 1 kHz, motor respondendo, watchdog satisfeito — e o Windows acusando "falha na solicitação de
// descritor". Reiniciar SÓ o processador, sem tocar na alimentação, ressuscitou o USB: prova de que
// a pilha trava em execução e não se recupera, e de que não é cabo, porta nem fonte.
//
// Falta saber ONDE ela trava, e são dois lugares possíveis com conserto bem diferente:
//   · a TAREFA (tud_task) — travada num mutex ou espera; a interrupção segue rodando
//   · a INTERRUPÇÃO — presa num laço; aí nada mais roda, nem a tarefa
//
// Dois contadores respondem isso sem alterar comportamento nenhum. Já erramos hoje mexendo num laço
// de interrupção por hipótese (a base entrou em ciclo de reset), então desta vez a ordem é: medir
// primeiro, mexer depois.
//
// COMO LER, com o USB morto: se `irq` sobe e `task` está parado, a tarefa travou. Se os dois estão
// parados, é a interrupção. Se os dois sobem e mesmo assim não enumera, o problema é de protocolo,
// não de travamento.
// ⚠️ Os contadores vivem no g_bb_trace (.noinit), NAO em variaveis proprias. Eu os criei soltos, a
// base reiniciou pelo watchdog, e no boot seguinte estavam ZERADOS — variavel comum e zerada pelo
// startup. Perdi exatamente a medicao que existia para decidir a hipotese. Ver blackbox.h.

// Task: roda tud_task + drena o CDC RX no buffer armado
static void usb_tusb_task(void*) {
    for (;;) {
        g_bb_trace.usb_task_ticks++;
        tud_task();
        if (s_rx.active && tud_cdc_available() > 0) {
            uint32_t n = tud_cdc_available();
            if (n > s_rx.size) n = s_rx.size;
            n = tud_cdc_read(s_rx.buf, n);
            if (n > 0) { s_rx.active = false; usb_rx_process_packet(s_rx.buf, n, s_rx.ep); }
        }
        osDelay(1);   // tud_task é não-bloqueante; 1ms basta pro ASCII
    }
}

// ODrive → CDC IN. Espera espaço e copia TUDO antes de retornar OK (senão o ODrive marca
// o stream como erro e trava no meio de uma resposta longa).
extern "C" uint8_t CDC_Transmit_FS(uint8_t* Buf, uint16_t Len, uint8_t endpoint_pair) {
    if (endpoint_pair == CDC_IN_EP) {
        if (!tud_cdc_connected()) return USBD_FAIL;
        uint32_t rem = Len, spin = 0; const uint8_t* p = Buf;
        while (rem > 0) {
            if (!tud_cdc_connected()) return USBD_FAIL;
            uint32_t n = tud_cdc_write(p, rem);
            if (n > 0) { p += n; rem -= n; spin = 0; tud_cdc_write_flush(); }
            else { tud_cdc_write_flush(); if (++spin > 100) return USBD_FAIL; osDelay(1); }
        }
        return USBD_OK;
    }
    // Endpoint nativo Fibre (não exposto no build CDC-only): finge OK + posta TX-done.
    if (endpoint_pair == ODRIVE_IN_EP) osMessagePut(usb_event_queue, 4, 0);
    return USBD_OK;
}

// ODrive arma o próximo RX; capturamos o buffer e drenamos na task.
extern "C" uint8_t USBD_CDC_ReceivePacket(USBD_HandleTypeDef* pdev, uint8_t* buf,
                                          uint16_t size, uint8_t endpoint_num) {
    (void)pdev;
    if (endpoint_num == CDC_OUT_EP) {
        s_rx.buf = buf; s_rx.size = size; s_rx.ep = endpoint_num; __DMB(); s_rx.active = true;
    }
    return USBD_OK;
}

// Substitui o init gerado pela ST. Setup do PHY OTG_FS (com o NOVBUSSENS do MKS) + task.
extern "C" void MX_USB_DEVICE_Init(void) {
    __HAL_RCC_GPIOA_CLK_ENABLE();
    GPIO_InitTypeDef g{};
    g.Pin = GPIO_PIN_11 | GPIO_PIN_12; g.Mode = GPIO_MODE_AF_PP; g.Pull = GPIO_NOPULL;
    g.Speed = GPIO_SPEED_FREQ_VERY_HIGH; g.Alternate = GPIO_AF10_OTG_FS;
    HAL_GPIO_Init(GPIOA, &g);
    __HAL_RCC_USB_OTG_FS_CLK_ENABLE();
    {   // CRÍTICO no MKS: VBUS (PA9) não está ligada → NOVBUSSENS, senão o PHY nunca assina presença.
        USB_OTG_GlobalTypeDef *U = (USB_OTG_GlobalTypeDef *)USB_OTG_FS_PERIPH_BASE;
        U->GCCFG |= USB_OTG_GCCFG_PWRDWN;         // acorda o PHY
        U->GCCFG |= USB_OTG_GCCFG_NOVBUSSENS;
        U->GCCFG &= ~(USB_OTG_GCCFG_VBUSASEN | USB_OTG_GCCFG_VBUSBSEN);
        *(volatile uint32_t *)(USB_OTG_FS_PERIPH_BASE + USB_OTG_PCGCCTL_BASE) = 0;
    }
    HAL_Delay(50);
    HAL_NVIC_SetPriority(OTG_FS_IRQn, 6, 0);   // >= MAX_SYSCALL (5) pro TinyUSB usar FromISR
    HAL_NVIC_EnableIRQ(OTG_FS_IRQn);
    usb_hid_report_desc_build();   // monta ffb+a0 (fonte única de tamanho) ANTES do tud_init
    tusb_init();
    osThreadDef(tusbTask, usb_tusb_task, osPriorityNormal, 0, 2048 / sizeof(StackType_t));
    osThreadCreate(osThread(tusbTask), NULL);
}

// ─────────────────────────────────────────────────────────────────────────────
// QUAL interrupcao do USB esta inundando a CPU.
//
// MEDIDO EM 15/08/2026: em operacao normal a ISR do USB entra ~1.121 vezes por segundo (batendo
// com o laco de 1 kHz). No instante em que a base reinicia sozinha, o rastro mostra ~23.020 por
// segundo — VINTE VEZES mais. Nao e "a pilha continua viva", como li antes: a ISR come a CPU, o
// laco de FFB nao roda, para de alimentar o watchdog e a placa reinicia 2 s depois.
//
// O que faltava era o NOME do evento. O DWC2 tem um bit por causa em GINTSTS; contando por bit,
// o culpado aparece nominalmente em vez de por deducao.
//
// ⚠️ ISTO SO CONTA. Nao mascara, nao limpa, nao muda a ordem de nada — o comportamento do USB fica
// identico. So o `act &= act - 1` (derruba o bit mais baixo) percorre os bits ATIVOS, tipicamente
// um ou dois, entao o custo por interrupcao e desprezivel mesmo a 23 kHz.
//
// Vive em .noinit com magic proprio: sobrevive ao reset — que e justamente o caso interessante —
// e zera quando a energia cai. Magic separado do BlackBoxTrace de proposito, para nao repetir o
// erro de mudar layout compartilhado e ler lixo com cara de medicao.
// ─────────────────────────────────────────────────────────────────────────────
#define DRVLAB_IRQBITS_MAGIC 0x1B175A20u   // +1 a cada mudanca de layout (ver blackbox.h)

struct DrvlabIrqBits {
    uint32_t magic;
    uint32_t bit[32];
    // ⚠️ A MEDIA ESCONDE O SURTO, e foi ela que me enganou em 15/08/2026: um boot que travou
    // mostrou 2.792 IRQ/s de media em 206 s — perto do normal —, enquanto outro, que durou 35 s,
    // mostrou 23.020/s. Um surto de dois segundos some numa media de tres minutos, e e o surto que
    // derruba o laco. Estes campos guardam a taxa INSTANTANEA e o PIOR valor ja visto.
    uint32_t taxa_atual;   // IRQ/s na ultima janela de 1 s
    uint32_t taxa_maxima;  // maior IRQ/s desde que a base foi ligada na tomada
    uint32_t irq_anterior; // marca da janela anterior (uso interno)
};
DrvlabIrqBits g_bb_irq_bits __attribute__((section(".noinit")));

/// Fecha uma janela de 1 s e guarda a taxa. Chamada pelo laco de FFB, que roda a 1 kHz — e por
/// isso a janela e simplesmente "mil voltas". Se o laco parar, o ultimo valor gravado fica sendo o
/// retrato de ate 1 s antes do travamento, que e exatamente o que queremos ler depois.
extern "C" void drvlab_irq_janela(uint32_t irq_total) {
    if (g_bb_irq_bits.magic != DRVLAB_IRQBITS_MAGIC) return;   // ainda nao inicializado pela ISR
    const uint32_t taxa = irq_total - g_bb_irq_bits.irq_anterior;
    g_bb_irq_bits.irq_anterior = irq_total;
    g_bb_irq_bits.taxa_atual = taxa;
    if (taxa > g_bb_irq_bits.taxa_maxima) g_bb_irq_bits.taxa_maxima = taxa;
}

static inline void irqbits_conta(void) {
    if (g_bb_irq_bits.magic != DRVLAB_IRQBITS_MAGIC) {
        g_bb_irq_bits.magic = DRVLAB_IRQBITS_MAGIC;
        for (int i = 0; i < 32; ++i) g_bb_irq_bits.bit[i] = 0;
        g_bb_irq_bits.taxa_atual = g_bb_irq_bits.taxa_maxima = g_bb_irq_bits.irq_anterior = 0;
    }
    USB_OTG_GlobalTypeDef *U = (USB_OTG_GlobalTypeDef *)USB_OTG_FS_PERIPH_BASE;
    uint32_t act = U->GINTSTS & U->GINTMSK;   // so o que esta ATIVO E habilitado
    while (act) {
        const int b = __builtin_ctz(act);
        g_bb_irq_bits.bit[b]++;
        act &= act - 1;
    }
}

extern "C" void OTG_FS_IRQHandler(void) { g_bb_trace.usb_irq_ticks++; irqbits_conta(); tud_int_handler(0); }

// Callbacks do TinyUSB → eventos do ODrive
extern "C" void tud_mount_cb(void)   { osMessagePut(usb_event_queue, 1, 0); }
extern "C" void tud_umount_cb(void)  { osMessagePut(usb_event_queue, 2, 0); }
extern "C" void tud_suspend_cb(bool) { osMessagePut(usb_event_queue, 2, 0); }
extern "C" void tud_resume_cb(void)  { osMessagePut(usb_event_queue, 1, 0); }
extern "C" void tud_cdc_tx_complete_cb(uint8_t) { osMessagePut(usb_event_queue, 3, 0); }

// ============================================================================================
// TU_BREAKPOINT desarmado — ver o porquê abaixo.
//
// O TinyUSB, ao encontrar uma condição inesperada, executa BKPT #0 — que SÓ tem efeito quando há
// depurador conectado. Em bancada de desenvolvimento de USB isso é útil; num volante é uma
// armadilha, porque o comportamento da placa passa a depender de o ST-Link estar plugado.
//
// Diagnosticado em 14/08/2026 depois de sessões caçando um travamento "aleatório" que só a tomada
// resolvia: o core estava em HALT DE DEPURAÇÃO (DHCSR=0x30003), parado dentro da ISR do USB, com o
// tick do FreeRTOS congelado e nenhum hard fault registrado. A ferramenta de diagnóstico é que
// provocava o defeito.
//
// A partir da 0.21 o TinyUSB oferece este ponto de extensão oficial (CFG_TUSB_DEBUG_BREAKPOINT), em
// vez de precisarmos patchar o vendor: ele chama esta função no lugar do BKPT. Não perdemos detecção
// de erro — o TU_ASSERT continua retornando na falha, que é a parte que trata o problema; o
// breakpoint só servia para chamar um humano com um depurador aberto.
extern "C" void drvlab_tusb_no_breakpoint(void) { /* de propósito, não faz nada */ }
