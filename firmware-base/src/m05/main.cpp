// ============================================================================
//  DriveLab Firmware
//  main.cpp (m05) — M0.5 v2 Passo A: esqueleto TinyUSB (joystick 1 eixo).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================

// DriveLab Firmware — M0.5 v2, Passo A (de-risco da pilha USB)
// Alvo: ODESC v4.2 (STM32F405). Framework: Arduino (STM32duino) + TinyUSB
// (Adafruit TinyUSB Library), substituindo o shim USBLibrarySTM32 que NUNCA
// chegou a enumerar no F405 (o host via um truncamento no descriptor e
// desistia). Esta é a segunda tentativa de decisão B2.
//
// OBJETIVO deste Passo A: provar SÓ a pilha TinyUSB — enumerar como um
// joystick MÍNIMO (1 eixo X de 16 bits + 1 botão), SEM PID/FFB ainda.
// Se isto enumerar no Mac (hidutil), a pilha está viva e o Passo B liga o
// descriptor PID/FFB de verdade (ffb_hid_descriptor.h, já pronto em include/).
//
// SEGURANÇA: continua SEM motor / sem estágio de potência. Só enumeração.

#include <Arduino.h>
#include <Adafruit_TinyUSB.h>

// ----------------------------------------------------------------------
// Report descriptor mínimo: gamepad padrão da própria lib (traz X/Y/Z/...
// + 32 botões). Simples e já validado pela Adafruit; usamos só o campo X
// e o botão 0 — os demais eixos/botões ficam parados em zero.
// ----------------------------------------------------------------------
static uint8_t const kHidReportDescriptor[] = {
    TUD_HID_REPORT_DESC_GAMEPAD()
};

Adafruit_USBD_HID g_hid(kHidReportDescriptor, sizeof(kHidReportDescriptor),
                         HID_ITF_PROTOCOL_NONE, 4, false);

void setup()
{
    // Identificação USB — VID 0x1209 (pid.codes, alocação open-source) /
    // PID 0x0001 (DriveLab Base, provisório p/ este de-risco).
    TinyUSBDevice.setID(0x1209, 0x0001);
    TinyUSBDevice.setManufacturerDescriptor("DriveLab");
    TinyUSBDevice.setProductDescriptor("DriveLab Base");

    // NOTA VBUS: o port STM32 desta lib (Adafruit_TinyUSB_stm32.cpp,
    // TinyUSB_Port_InitDevice) já desliga o sensing de VBUS incondicionalmente
    // (GCCFG NOVBUSSENS/VBUSBSEN/VBUSASEN) — necessário pois a ODESC não traz
    // PA9 ligado ao VBUS. Nenhuma chamada extra é necessária aqui.

    g_hid.begin();

    // Sobe a pilha TinyUSB (equivalente a TinyUSBDevice.begin(0)).
    TinyUSB_Device_Init(0);

    // Passo A é só HID (sem CDC) — sem Serial aqui de propósito: o core do
    // STM32duino já mapeia "Serial" -> UART física (Serial4 nesta board), e
    // esta lib redefine a macro "Serial" -> SerialTinyUSB quando USE_TINYUSB
    // está definido, o que colide com o core se CDC não estiver habilitado
    // no descriptor (não é o caso aqui: só HID). Debug de bancada fica p/
    // depois (via CDC, se necessário).
}

void loop()
{
    // Mantém a pilha TinyUSB viva. TinyUSB_Device_Task() só existe/roda em
    // alguns cores (weak); TinyUSBDevice.task() é o caminho garantido aqui.
    TinyUSBDevice.task();

    if (!TinyUSBDevice.mounted())
    {
        delay(2);
        return;
    }

    // Report de gamepad: eixo X variando devagar (prova que o host VÊ o
    // dispositivo se mexendo), demais campos zerados/centrados.
    static uint32_t lastSend = 0;
    uint32_t now = millis();
    if (g_hid.ready() && (now - lastSend >= 10))
    {
        lastSend = now;

        hid_gamepad_report_t report;
        memset(&report, 0, sizeof(report));
        report.x = (int8_t)(127.0f * sinf(now / 1000.0f));

        g_hid.sendReport(0, &report, sizeof(report));
    }
}
