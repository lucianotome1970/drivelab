// ============================================================================
//  DriveLab Firmware
//  usb_base.h — Setup da pilha USB (TinyUSB) composite HID (FFB + canal A0
//  no MESMO relatório combinado, ver comentário em usb_base.cpp) + CDC,
//  extraído do monolito src/m05/main.cpp (M5 Stage 0, Task 3). Reusável por
//  futuros firmwares (M5) sem duplicar a montagem do descritor/init/re-enum.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cstdint>

#include <Adafruit_TinyUSB.h>

namespace UsbBase
{
    // Callbacks de GET/SET_REPORT — mesma assinatura de
    // Adafruit_USBD_HID::setReportCallback (hid_get_report_callback_t /
    // hid_set_report_callback_t, ver Adafruit_USBD_HID.h).
    using GetReportCallback = uint16_t (*)(uint8_t report_id, hid_report_type_t report_type,
                                            uint8_t *buffer, uint16_t reqlen);
    using SetReportCallback = void (*)(uint8_t report_id, hid_report_type_t report_type,
                                        uint8_t const *buffer, uint16_t bufsize);

    // Registra os callbacks de GET/SET_REPORT — chamar ANTES de begin() (mesma
    // ordem do main.cpp original: setReportCallback() antes de g_hid.begin()),
    // para já valerem assim que o host mandar/pedir algo.
    void setReportCallbacks(GetReportCallback getCb, SetReportCallback setCb);

    // Monta o Report Descriptor combinado (FFB, ffb_hid_report_desc, seguido
    // do canal A0, a0_hid_descriptor.h — mesma interface HID, ver comentário
    // completo em usb_base.cpp), faz TinyUSBDevice.begin(0) (se ainda não
    // inicializada) + g_hid.begin() + o padrão de detach/attach para forçar
    // re-enumeração, e por fim SerialTinyUSB.begin(115200). Chamar uma vez em
    // setup(), DEPOIS de dfuCheckAtBootOrJump() (dfu_jump.h) e DEPOIS de
    // setReportCallbacks(). Devolve referência ao Adafruit_USBD_HID pronto
    // (para sendReport()/ready() no loop(), embora sendReport() abaixo já
    // cubra o caso comum).
    Adafruit_USBD_HID &begin();

    // Wrapper de g_hid.sendReport() — checa g_hid.ready() ele mesmo e só
    // chama sendReport() (e devolve true) se o endpoint IN estiver livre,
    // senão devolve false sem efeito colateral (mesmo padrão "um sendReport()
    // por janela de EP" do fix P0/HID EP, ver MEMORY).
    bool sendReport(uint8_t reportId, const uint8_t *payload, uint16_t len);
} // namespace UsbBase
