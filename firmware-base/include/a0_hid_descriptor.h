// ============================================================================
//  DriveLab Firmware
//  a0_hid_descriptor.h — Descritor HID Report do canal de configuração "A0"
//  (vendor, usage-page 0xFF00), servido pela base wheelbase via TinyUSB numa
//  2ª interface HID (IF1), separada da IF0 (FFB). Payload de 63 bytes por
//  report (ReportConstants.ReportSize) — mesma estrutura do protocolo P0 já
//  usado no firmware-pedal (ver firmware-pedal/src/main.cpp:79-94), só que
//  com os Report IDs do canal A0 da base.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <stdint.h>

// Report IDs do canal A0 (Input = device->host, Output = host->device).
#define A0_RID_STATE      0x01  // Input  — estado/telemetria do canal A0
#define A0_RID_CMD        0x02  // Output — comando
#define A0_RID_DIRECT     0x10  // Output — escrita direta
#define A0_RID_SETWRITE   0x14  // Output — grava setting
#define A0_RID_SETREAD    0x15  // Output — pede leitura de setting
#define A0_RID_SETVALUE   0x16  // Input  — resposta de leitura de setting

// Application collection, usage-page 0xFF00 (vendor-defined) — nenhum outro
// software/driver reivindica essa página, então o Windows/Linux entrega a
// interface crua para nós (sem HID class driver genérico tentando parseá-la
// como joystick/teclado).
const uint8_t a0_hid_report_desc[] = {
    0x06, 0x00, 0xFF,             // Usage Page (Vendor Defined 0xFF00)
    0x09, 0x01,                   // Usage (0x01)
    0xA1, 0x01,                   // Collection (Application)
      0x15, 0x00,                 //   Logical Minimum (0)
      0x26, 0xFF, 0x00,           //   Logical Maximum (255)
      0x75, 0x08,                 //   Report Size (8)
      0x95, 0x3F,                 //   Report Count (63) — payload + Report ID = 64 (cabe no EP HID)

      0x85, A0_RID_STATE,         //   Report ID (0x01)
      0x09, 0x01,                 //   Usage (0x01)
      0x81, 0x02,                 //   Input (Data,Var,Abs)

      0x85, A0_RID_SETVALUE,      //   Report ID (0x16)
      0x09, 0x02,                 //   Usage (0x02)
      0x81, 0x02,                 //   Input (Data,Var,Abs)

      0x85, A0_RID_CMD,           //   Report ID (0x02)
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
