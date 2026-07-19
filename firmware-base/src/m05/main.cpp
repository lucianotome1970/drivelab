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
    // Identificação USB (VID 0x1209 pid.codes / PID 0x0001 / strings
    // "DriveLab" / "DriveLab Base") vem de -D USB_VID/USB_PID/USB_MANUFACTURER/
    // USB_PRODUCT no platformio.ini, NÃO de TinyUSBDevice.setID() aqui.
    // Achado no bring-up: Adafruit_USBD_Device::begin() chama
    // clearConfiguration() incondicionalmente, que reconstrói o device
    // descriptor a partir dessas macros de build — qualquer setID()/
    // setProductDescriptor() chamado ANTES de begin() era descartado. Era
    // por isso que a v1 deste Passo A enumerava como 0x239A/0xCAFE
    // "GENERIC_F405RGTX" (defaults da lib Adafruit / ARDUINO_BOARD do core
    // STM32duino), mesmo chamando setID() no início do setup().
    //
    // NOTA VBUS: o port STM32 desta lib (Adafruit_TinyUSB_stm32.cpp,
    // TinyUSB_Port_InitDevice) já desliga o sensing de VBUS incondicionalmente
    // (GCCFG NOVBUSSENS/VBUSBSEN/VBUSASEN) — necessário pois a ODESC não traz
    // PA9 ligado ao VBUS. Nenhuma chamada extra é necessária aqui.

    // Padrão oficial da Adafruit_TinyUSB_Arduino p/ cores sem auto-init da
    // pilha (ver examples/HID/hid_gamepad/hid_gamepad.ino): begin() explícito
    // ANTES de registrar qualquer classe HID/CDC extra.
    if (!TinyUSBDevice.isInitialized())
    {
        TinyUSBDevice.begin(0);
    }

    // NOTA CDC: Adafruit_USBD_Device::begin() SEMPRE registra um CDC
    // ("Serial is always added by default" — Adafruit_USBD_Device.cpp) antes
    // de qualquer classe nossa entrar. Era por isso que a v1 via só CDC no
    // config descriptor: g_hid.begin() rodava ANTES de TinyUSB_Device_Init(0),
    // e o clearConfiguration() de dentro de begin() descartava a interface
    // HID já registrada. Um composite HID+CDC é aceitável aqui (e útil p/
    // debug futuro via CDC).
    g_hid.begin();

    // Se a pilha já montou só com o CDC default antes do HID entrar, o host
    // não percebe a interface nova sem uma re-enumeração — força via
    // detach/attach (padrão oficial da lib).
    if (TinyUSBDevice.mounted())
    {
        TinyUSBDevice.detach();
        delay(10);
        TinyUSBDevice.attach();
    }

    // Passo A não usa Serial/CDC no código (sem chamadas de log aqui de
    // propósito) — a lib redefine a macro "Serial" -> SerialTinyUSB quando
    // USE_TINYUSB está definido; debug de bancada fica p/ depois.
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
