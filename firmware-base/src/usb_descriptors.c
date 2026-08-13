// firmware-base — descritores USB TinyUSB (STAGE 3a: CDC + HID composite).
// O device se apresenta como "DriveLab Base" com DUAS funções:
//   - HID (interface 0): Joystick + PID/Force-Feedback → aparece em "Controladores
//     de Jogo" do Windows e é reconhecido pelo DriveLab Studio. Descriptor = o NOSSO
//     ffb_hid_descriptor.h (mesmo do firmware-base, provado com ACC/AMS2/EVO).
//   - CDC (interfaces 1,2): serial (config/telemetria/calibração pelo nosso app).
// Ordem HID-antes-de-CDC é DELIBERADA (Windows é sensível à ordem das interfaces).
// Derivado dos exemplos TinyUSB (MIT). Autor: Luciano Tomé <lucianotome1970@gmail.com> — MIT.
#include "tusb.h"
#include <string.h>
#include "ffb_hid_descriptor.h"   // ffb_hid_report_desc[] (joystick+PID FFB) — nesta TU
#include "a0_hid_descriptor.h"    // a0_hid_report_desc[] (canal A0 vendor 0xFF00 do app) — nesta TU

// Identidade NOSSA (VID pid.codes 0x1209, = firmware-base → DriveLab Studio reconhece).
#define USB_VID   0x1209
#define USB_PID   0x0001
#define USB_BCD   0x0100

static const tusb_desc_device_t desc_device = {
    .bLength            = sizeof(tusb_desc_device_t),
    .bDescriptorType    = TUSB_DESC_DEVICE,
    .bcdUSB             = 0x0200,
    // Composite com IAD (a função CDC precisa do Interface Association Descriptor).
    .bDeviceClass       = TUSB_CLASS_MISC,
    .bDeviceSubClass    = MISC_SUBCLASS_COMMON,
    .bDeviceProtocol    = MISC_PROTOCOL_IAD,
    .bMaxPacketSize0    = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor           = USB_VID,
    .idProduct          = USB_PID,
    .bcdDevice          = USB_BCD,
    .iManufacturer      = 0x01,
    .iProduct           = 0x02,
    .iSerialNumber      = 0x03,
    .bNumConfigurations = 0x01
};
uint8_t const * tud_descriptor_device_cb(void) { return (uint8_t const *)&desc_device; }

// --- Report descriptor do HID = ffb_hid_report_desc + a0_hid_report_desc concatenados.
// FIX da enumeração (por que o A0 quebrou o Windows antes): o `wDescriptorLength` do Config
// Descriptor (HID_REPORT_DESC_LEN abaixo) e os bytes entregues em tud_hid_descriptor_report_cb
// SAEM DO MESMO `sizeof(s_hid_report_desc)` → impossível divergir. Preenchido ANTES do tud_init
// (usb_hid_report_desc_build, chamado de MX_USB_DEVICE_Init) + rede lazy no callback. ---
static uint8_t s_hid_report_desc[sizeof(ffb_hid_report_desc) + sizeof(a0_hid_report_desc)];
#define HID_REPORT_DESC_LEN (sizeof(s_hid_report_desc))
static uint8_t s_hid_desc_ready = 0;

void usb_hid_report_desc_build(void) {   // chamar ANTES do tud_init()
    memcpy(s_hid_report_desc, ffb_hid_report_desc, sizeof(ffb_hid_report_desc));
    memcpy(s_hid_report_desc + sizeof(ffb_hid_report_desc), a0_hid_report_desc, sizeof(a0_hid_report_desc));
    s_hid_desc_ready = 1;
}

uint8_t const * tud_hid_descriptor_report_cb(uint8_t instance) {
    (void)instance;
    if (!s_hid_desc_ready) usb_hid_report_desc_build();   // garante preenchido antes de entregar
    return s_hid_report_desc;
}

// --- Layout de interfaces + endpoints do composite ---
enum {
    ITF_NUM_HID = 0,        // HID (joystick + FFB)  — PRIMEIRO
    ITF_NUM_CDC_CTRL,       // CDC control
    ITF_NUM_CDC_DATA,       // CDC data
    ITF_NUM_TOTAL
};

// Endpoints (STM32F405 OTG_FS: 4 IN + 4 OUT incluindo EP0):
//   HID  : IN 0x81 / OUT 0x01  (bInterval=1 → 1kHz, pegadinha #10 da receita)
//   CDC  : NOTIF 0x82 / OUT 0x02 / IN 0x83
#define EPNUM_HID_OUT     0x01
#define EPNUM_HID_IN      0x81
#define EPNUM_CDC_NOTIF   0x82
#define EPNUM_CDC_OUT     0x02
#define EPNUM_CDC_IN      0x83

#define CONFIG_TOTAL_LEN  (TUD_CONFIG_DESC_LEN + TUD_HID_INOUT_DESC_LEN + TUD_CDC_DESC_LEN)

static uint8_t desc_configuration[] = {
    TUD_CONFIG_DESCRIPTOR(1, ITF_NUM_TOTAL, 0, CONFIG_TOTAL_LEN,
        TUSB_DESC_CONFIG_ATT_REMOTE_WAKEUP | TUSB_DESC_CONFIG_ATT_SELF_POWERED, 100),
    // HID (joystick+FFB+A0): comprimento do report descriptor = ffb + a0 concatenados.
    TUD_HID_INOUT_DESCRIPTOR(ITF_NUM_HID, 5 /* iInterface */, HID_ITF_PROTOCOL_NONE,
        HID_REPORT_DESC_LEN, EPNUM_HID_OUT, EPNUM_HID_IN, 64, 1),
    // CDC (serial)
    TUD_CDC_DESCRIPTOR(ITF_NUM_CDC_CTRL, 4 /* iInterface */, EPNUM_CDC_NOTIF, 8,
                       EPNUM_CDC_OUT, EPNUM_CDC_IN, 64)
};
uint8_t const * tud_descriptor_configuration_cb(uint8_t index) { (void)index; return desc_configuration; }

static const char* string_desc_arr[] = {
    (const char[]){0x09, 0x04},   // 0: en-US
    "DriveLab",                   // 1: Manufacturer
    "DriveLab Base",              // 2: Product
    "0001",                       // 3: Serial
    "DriveLab Base CDC",          // 4: CDC interface
    "DriveLab Base FFB"           // 5: HID interface
};
static uint16_t desc_str_buf[32];
uint16_t const* tud_descriptor_string_cb(uint8_t index, uint16_t langid) {
    (void)langid;
    if (index == 0) { desc_str_buf[0] = (TUSB_DESC_STRING << 8) | 4; desc_str_buf[1] = 0x0409; return desc_str_buf; }
    if (index >= sizeof(string_desc_arr)/sizeof(string_desc_arr[0])) return NULL;
    const char* str = string_desc_arr[index];
    size_t chars = strlen(str); if (chars > 31) chars = 31;
    desc_str_buf[0] = (TUSB_DESC_STRING << 8) | (2*chars + 2);
    for (size_t i = 0; i < chars; i++) desc_str_buf[1+i] = (uint16_t)str[i];
    return desc_str_buf;
}
