// ============================================================================
//  DriveLab Firmware
//  a0_hid_descriptor.h — Descritor HID Report do canal de configuração "A0"
//  (vendor, usage-page 0xFF00), APENSADO ao descritor FFB (ffb_hid_descriptor.h)
//  numa ÚNICA interface HID (IF0), ao lado da CDC (IF1). Payload de 63 bytes
//  por report (ReportConstants.ReportSize) — mesma estrutura do protocolo P0
//  já usado no firmware-pedal (ver firmware-pedal/src/main.cpp:79-94), só que
//  com os Report IDs do canal A0 da base.
//
//  Achado de bancada (redo do Task 2): o STM32F405 OTG_FS só tem ~3 endpoints
//  IN utilizáveis — CDC consome 2, o HID FFB consome 1; uma 2ª interface HID
//  (a versão original deste Task) precisaria de um 4º IN que não existe. Por
//  isso o canal A0 passa a viver na MESMA interface HID do FFB: o descritor
//  final servido por g_hid é a concatenação de ffb_hid_report_desc (Task 3) +
//  a0_hid_report_desc (este arquivo) — ver main.cpp/setup(). Os Report IDs
//  0x01/0x02 (RID_JOYSTICK/RID_PID_STATE) já são usados pelo FFB, então o
//  canal A0 foi remapeado para 0x21 (Input, era DeviceState) e 0x22 (Output,
//  era Command); os demais (0x10/0x14/0x15/0x16) já estavam livres no
//  descritor FFB (que usa 0x01-0x06,0x0A-0x0D,0x11-0x13,0xA1).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <stdint.h>

// Report IDs do canal A0 (Input = device->host, Output = host->device).
// Remapeados p/ não colidir com ffb_hid_report_desc (RID_JOYSTICK=0x01,
// RID_PID_STATE=0x02, ..., RID_PID_POOL=0x13, RID_VENDOR_HID_CMD=0xA1).
#define A0_RID_STATE      0x21  // Input  — estado/telemetria do canal A0 (era 0x01)
#define A0_RID_SETVALUE   0x16  // Input  — resposta de leitura de setting
#define A0_RID_CMD        0x22  // Output — comando (era 0x02)
#define A0_RID_DIRECT     0x10  // Output — escrita direta
#define A0_RID_SETWRITE   0x14  // Output — grava setting
#define A0_RID_SETREAD    0x15  // Output — pede leitura de setting

// Application collection, usage-page 0xFF00 (vendor-defined) — nenhum outro
// software/driver reivindica essa página, então o Windows/Linux entrega os
// reports crus para nós (sem HID class driver genérico tentando parseá-los
// como joystick/teclado). Apensada ao final de ffb_hid_report_desc (ver
// main.cpp) — por isso NÃO abre um novo descritor de Application "solto":
// é só mais uma Application collection dentro do mesmo Report Descriptor,
// o que é válido em HID (um Report Descriptor pode ter várias top-level
// collections; o host as trata como "usages" distintos da mesma interface).
const uint8_t a0_hid_report_desc[] = {
    0x06, 0x00, 0xFF,             // Usage Page (Vendor Defined 0xFF00)
    0x09, 0x01,                   // Usage (0x01)
    0xA1, 0x01,                   // Collection (Application)
      0x15, 0x00,                 //   Logical Minimum (0)
      0x26, 0xFF, 0x00,           //   Logical Maximum (255)
      0x75, 0x08,                 //   Report Size (8)
      0x95, 0x3F,                 //   Report Count (63) — payload + Report ID = 64 (cabe no EP HID)

      0x85, A0_RID_STATE,         //   Report ID (0x21)
      0x09, 0x01,                 //   Usage (0x01)
      0x81, 0x02,                 //   Input (Data,Var,Abs)

      0x85, A0_RID_SETVALUE,      //   Report ID (0x16)
      0x09, 0x02,                 //   Usage (0x02)
      0x81, 0x02,                 //   Input (Data,Var,Abs)

      0x85, A0_RID_CMD,           //   Report ID (0x22)
      0x09, 0x03,                 //   Usage (0x03)
      0x91, 0x02,                 //   Output (Data,Var,Abs)

      0x85, A0_RID_DIRECT,        //   Report ID (0x10)
      0x09, 0x04,                 //   Usage (0x04)
      0x91, 0x02,                 //   Output (Data,Var,Abs)

      0x85, A0_RID_SETWRITE,      //   Report ID (0x14)
      0x09, 0x05,                 //   Usage (0x05)
      0x91, 0x02,                 //   Output (Data,Var,Abs)

      0x85, A0_RID_SETREAD,       //   Report ID (0x15)
      0x09, 0x06,                 //   Usage (0x06)
      0x91, 0x02,                 //   Output (Data,Var,Abs)
    0xC0                          // End Collection
};

const uint16_t a0_hid_report_desc_len = sizeof(a0_hid_report_desc);
