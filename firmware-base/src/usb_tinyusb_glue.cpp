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
volatile uint32_t g_usb_task_ticks = 0;   // voltas da tarefa do TinyUSB
volatile uint32_t g_usb_irq_ticks  = 0;   // entradas na interrupção do USB

// Task: roda tud_task + drena o CDC RX no buffer armado
static void usb_tusb_task(void*) {
    for (;;) {
        g_usb_task_ticks++;
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

extern "C" void OTG_FS_IRQHandler(void) { g_usb_irq_ticks++; tud_int_handler(0); }

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
