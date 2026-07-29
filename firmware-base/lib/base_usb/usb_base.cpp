// ============================================================================
//  DriveLab Firmware
//  usb_base.cpp — Implementação do setup da pilha USB (TinyUSB). Extraído do
//  monolito src/m05/main.cpp (M5 Stage 0, Task 3) — comportamento IDÊNTICO
//  ao original (mesmo descritor combinado, mesma ordem de init/re-enum, ver
//  comentários de bancada mantidos como registro).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

#include "usb_base.h"

#include <Arduino.h>
#include <cstring>

#include "ffb_hid_descriptor.h"
#include "a0_hid_descriptor.h"

namespace UsbBase
{
    namespace
    {
        // has_out_endpoint = true: o descritor completo tem vários Output
        // reports (Set Effect/Envelope/Condition/Periodic/Constant Force/Ramp
        // Force/Effect Operation/Block Free/Device Control/Device Gain/...)
        // que só chegam pelo endpoint OUT dedicado — sem isso a
        // Adafruit_USBD_HID monta só a variante IN-only do descritor de
        // interface (TUD_HID_DESCRIPTOR) e o host não tem como mandar
        // SET_REPORT via endpoint (só via control transfer, que games não
        // costumam usar para efeitos de FFB em tempo real).
        // Achado de bancada (redo do Task 2): o OTG_FS do STM32F405 só tem
        // ~3 endpoints IN utilizáveis (CDC consome 2, FFB 1) — não sobra um
        // 4º IN para uma 2ª interface HID. Por isso o canal A0 (vendor,
        // usage-page 0xFF00, ver a0_hid_descriptor.h) NÃO é mais uma
        // interface separada: seus reports são apensados ao final do Report
        // Descriptor do FFB e servidos pela MESMA interface HID (g_hid) — o
        // buffer combinado é montado em begin() (ver g_combined_hid_report_desc)
        // e atribuído a g_hid via setReportDescriptor() antes do g_hid.begin().
        // has_out_endpoint=true continua necessário: tanto o FFB (Set
        // Effect/Envelope/.../Device Control/Device Gain) quanto o A0
        // (A0_RID_CMD/DIRECT/SETWRITE/SETREAD) têm Output reports que só
        // chegam pelo endpoint OUT dedicado.
        // bInterval 1ms (1000Hz), NÃO 4ms (250Hz) — FIX 400Hz (2026-07-29): o intervalo
        // de 4ms limitava o endpoint a 250Hz, EXATAMENTE entre 200Hz (funciona) e 400Hz
        // (travava): o ACC a 400Hz manda FFB a cada 2.5ms mas o endpoint só aceitava a
        // cada 4ms → backpressure → ACC trava. FFBeast/Moza usam 1ms. Ver
        // [[drivelab-game-compat-a0]].
        Adafruit_USBD_HID g_hid(ffb_hid_report_desc, ffb_hid_report_desc_len,
                                 HID_ITF_PROTOCOL_NONE, 1, /*has_out_endpoint=*/true);

        // Buffer estático (precisa sobreviver ao runtime — tud_hid_descriptor_report_cb
        // devolve o ponteiro guardado por g_hid a qualquer momento, inclusive
        // bem depois do begin() retornar) com o Report Descriptor combinado:
        // FFB (Joystick + PID/Force Feedback, Task 3) seguido do canal A0
        // (vendor, Task 2). Preenchido em begin() antes de g_hid.begin() — ver
        // comentário lá.
        uint8_t g_combined_hid_report_desc[ffb_hid_report_desc_len + a0_hid_report_desc_len];
    } // namespace

    void setReportCallbacks(GetReportCallback getCb, SetReportCallback setCb)
    {
        g_hid.setReportCallback(getCb, setCb);
    }

    Adafruit_USBD_HID &begin()
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

        // COMPAT COM JOGOS (2026-07-28): o Adafruit_USBD_Device::begin() SEMPRE
        // adiciona um CDC (Serial) E seta bDeviceClass=0xEF (Misc/IAD, "composto").
        // Essa classe fazia o AMS2 IGNORAR a placa e o ACC engasgar. Removemos o CDC
        // e voltamos a classe pra 0x00 (device HID LIMPO, = FFBeast/Moza) com
        // clearConfiguration(): ele reseta _desc_device (bDeviceClass=0) e zera a
        // contagem de interfaces/endpoints (some o CDC). Só DEPOIS adicionamos o(s)
        // HID. O CDC de debug virou ring buffer em RAM lido por SWD (dbg_ring.h).
        // Ver [[drivelab-game-compat-a0]].
        TinyUSBDevice.clearConfiguration();
        // Monta o Report Descriptor combinado (FFB + A0, ver comentário junto de
        // g_combined_hid_report_desc) e o atribui a g_hid ANTES do begin() —
        // setReportDescriptor() só troca o ponteiro/tamanho guardados na
        // instância (Adafruit_USBD_HID.cpp), então a ordem aqui não afeta a
        // pilha diretamente, mas mantém a montagem e o begin() juntos e claros.
        memcpy(g_combined_hid_report_desc, ffb_hid_report_desc, ffb_hid_report_desc_len);
        memcpy(g_combined_hid_report_desc + ffb_hid_report_desc_len,
               a0_hid_report_desc, a0_hid_report_desc_len);
        g_hid.setReportDescriptor(g_combined_hid_report_desc, sizeof(g_combined_hid_report_desc));

        // Callbacks de GET/SET_REPORT já devem ter sido registrados via
        // setReportCallbacks() (chamado pelo main.cpp ANTES de begin()) —
        // para já valerem assim que o host mandar/pedir algo.

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

        // (SEM SerialTinyUSB.begin(): o CDC foi removido pelo clearConfiguration()
        // acima. Debug agora é o ring buffer em RAM lido por SWD — ver dbg_ring.h.)
        return g_hid;
    }

    bool sendReport(uint8_t reportId, const uint8_t *payload, uint16_t len)
    {
        if (!g_hid.ready())
        {
            return false;
        }
        return g_hid.sendReport(reportId, payload, len);
    }
} // namespace UsbBase
