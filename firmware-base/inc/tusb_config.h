// ============================================================================
//  DriveLab
//  tusb_config.h — Configuracao do TinyUSB para a base.
//
//  Os nomes CFG_TUD_*/CFG_TUSB_* sao a API do proprio TinyUSB; o que esta aqui
//  sao as ESCOLHAS da nossa base. As que importam:
//
//  · Duas interfaces ao mesmo tempo, CDC e HID. A HID e o volante (efeitos de
//    FFB + o nosso canal A0 de configuracao); a CDC e o console de diagnostico,
//    que na bancada substitui o SWD — e a licao mais cara que este projeto teve,
//    porque parar o nucleo por SWD DERRUBA o motor armado.
//  · Nada de MSC, MIDI, audio ou vendor: cada classe ligada custa endpoint e
//    RAM, e a base nao tem uso para nenhuma delas.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef _TUSB_CONFIG_H_
#define _TUSB_CONFIG_H_

#ifdef __cplusplus
extern "C" {
#endif

// A base e um STM32F405: nucleo USB OTG_FS, so full-speed.
#define CFG_TUSB_MCU              OPT_MCU_STM32F4
#define BOARD_DEVICE_RHPORT_NUM   0
#define BOARD_DEVICE_RHPORT_SPEED OPT_MODE_FULL_SPEED
#define CFG_TUSB_RHPORT0_MODE     (OPT_MODE_DEVICE | BOARD_DEVICE_RHPORT_SPEED)

// O laco do TinyUSB roda como tarefa do FreeRTOS, junto com o de FFB.
#define CFG_TUSB_OS               OPT_OS_FREERTOS

#define CFG_TUSB_MEM_SECTION
#define CFG_TUSB_MEM_ALIGN        __attribute__ ((aligned(4)))

#define CFG_TUD_ENDPOINT0_SIZE    64

// CDC (console) + HID (volante). O resto desligado — ver o cabecalho.
#define CFG_TUD_CDC               1
#define CFG_TUD_MSC               0
#define CFG_TUD_MIDI              0
#define CFG_TUD_HID               1
#define CFG_TUD_VENDOR            0
#define CFG_TUD_AUDIO             0
#define CFG_TUD_DFU_RT            0

// Fila da CDC folgada: o diagnostico sai em rajada (um bloco de estado por vez),
// e fila curta perde linha justamente quando ha mais o que ler.
#define CFG_TUD_CDC_RX_BUFSIZE    512
#define CFG_TUD_CDC_TX_BUFSIZE    512

// 64 bytes = o maximo de um endpoint full-speed, e o tamanho dos nossos reports:
// 1 byte de report ID + 63 de payload, tanto no FFB quanto no canal A0.
#define CFG_TUD_HID_EP_BUFSIZE    64

#ifdef __cplusplus
}
#endif

#endif
