// ============================================================================
//  DriveLab Firmware
//  main.cpp (m05) — M0.5 v2 Passo B: descritor HID PID completo (FFB).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================

// DriveLab Firmware — M0.5 v2, Passo B (descritor HID PID completo)
// Alvo: ODESC v4.2 (STM32F405). Framework: Arduino (STM32duino) + TinyUSB
// (Adafruit TinyUSB Library).
//
// O Passo A (ver git log) provou a pilha TinyUSB isolada com um joystick
// MÍNIMO (1 eixo + 1 botão) — enumerou OK no Mac e no Windows. Este Passo B
// troca o descritor mínimo pelo HID PID (Force Feedback) COMPLETO, gerado a
// partir do OpenFFBoard (ffb_hid_descriptor.h, Task 3): 1196 bytes, várias
// collections de Output (Set Effect/Envelope/Condition/Periodic/Constant
// Force/Ramp Force/...) além do Input do joystick (RID_JOYSTICK). O objetivo
// é só provar que o descritor sai INTEIRO do device (sem truncar) e que o
// Windows mostra a aba Force Feedback — o parser dos relatórios OUT/Set
// Report de efeitos FFB de verdade é a Task 6 (aqui é só um stub no-op).
//
// SEGURANÇA: continua SEM motor / sem estágio de potência. Só enumeração +
// um report de Input periódico "morto" (zerado/centrado).

#include <Arduino.h>
#include <Adafruit_TinyUSB.h>
#include "ffb_hid_descriptor.h"

// ----------------------------------------------------------------------
// Layout do report de Input do RID_JOYSTICK (collection Physical dentro da
// Application "Joystick" no início de ffb_hid_report_desc — ver Task 3):
//   - Usage Page(Button), Usage Min 1 / Max 64, Report Size 1, Report
//     Count 64, Input(Data,Var,Abs)  -> 64 bits = 8 bytes de botões.
//   - Usage Page(Generic Desktop), 8x eixos (X,Y,Z,Rx,Ry,Rz,Slider,Dial),
//     Logical Min -32767 / Max 32767, Report Size 16, Report Count 8,
//     Input(Data,Var,Abs)            -> 8 * 16 bits = 16 bytes de eixos.
// Total do payload (sem contar o byte de Report ID, que o TinyUSB
// antepõe sozinho via sendReport(report_id, ...)): 8 + 16 = 24 bytes.
// ----------------------------------------------------------------------
struct JoystickInputReport
{
    uint8_t buttons[8];  // 64 botões (bitmask), todos soltos = 0.
    int16_t axes[8];     // X,Y,Z,Rx,Ry,Rz,Slider,Dial — centrados = 0.
};
static_assert(sizeof(JoystickInputReport) == 24,
              "Payload do RID_JOYSTICK deve ter 24 bytes (8 botões + 8 eixos x16b)");

// ----------------------------------------------------------------------
// Callback de SET_REPORT / dados no endpoint OUT: por enquanto NO-OP. O
// decode real dos relatórios PID (Set Effect, Set Envelope, Effect
// Operation, ...) fica para a Task 6 (ffb_report.*). Aqui só evita que o
// host trave esperando resposta / a pilha ignore os OUT reports.
// ----------------------------------------------------------------------
static void hid_set_report_callback(uint8_t report_id,
                                     hid_report_type_t report_type,
                                     uint8_t const *buffer, uint16_t bufsize)
{
    (void)report_id;
    (void)report_type;
    (void)buffer;
    (void)bufsize;
    // Passo B: intencionalmente vazio (stub). Task 6 faz o parse de verdade.
}

// has_out_endpoint = true: o descritor completo tem vários Output reports
// (Set Effect/Envelope/Condition/Periodic/Constant Force/Ramp Force/Effect
// Operation/Block Free/Device Control/Device Gain/...) que só chegam pelo
// endpoint OUT dedicado — sem isso a Adafruit_USBD_HID monta só a variante
// IN-only do descritor de interface (TUD_HID_DESCRIPTOR) e o host não tem
// como mandar SET_REPORT via endpoint (só via control transfer, que games
// não costumam usar para efeitos de FFB em tempo real).
Adafruit_USBD_HID g_hid(ffb_hid_report_desc, ffb_hid_report_desc_len,
                         HID_ITF_PROTOCOL_NONE, 4, /*has_out_endpoint=*/true);

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
    // Stub no-op para SET_REPORT/OUT (ver comentário acima da callback) —
    // registrado ANTES do begin() para já valer assim que o host mandar algo.
    g_hid.setReportCallback(nullptr, hid_set_report_callback);

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

    // Report de Input do RID_JOYSTICK: eixo X variando devagar (prova que o
    // host VÊ o dispositivo se mexendo), demais campos zerados/centrados.
    // Passo B não decodifica FFB ainda — só serve o descritor inteiro e
    // mantém um Input válido fluindo.
    static uint32_t lastSend = 0;
    uint32_t now = millis();
    if (g_hid.ready() && (now - lastSend >= 10))
    {
        lastSend = now;

        JoystickInputReport report;
        memset(&report, 0, sizeof(report));
        report.axes[0] = (int16_t)(32767.0f * sinf(now / 1000.0f)); // X

        g_hid.sendReport(RID_JOYSTICK, &report, sizeof(report));
    }
}
