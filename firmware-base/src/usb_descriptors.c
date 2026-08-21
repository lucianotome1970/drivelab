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
// ============================================================================================
// IDENTIDADE DA BASE — a troca de PRODUTO foi TESTE, e o teste terminou
// ============================================================================================
// Em 20/08/2026 trocamos o produto (0x0001 -> 0x0010, "DriveLab Base" -> "DriveLab DD") para tirar
// o PC do caminho enquanto cacavamos os travamentos de USB: o Windows guarda o que aprendeu
// indexado por FABRICANTE + PRODUTO + numero de serie, e nem chega a reler as descricoes de um
// aparelho que ele julga ja conhecer — trocar so o nome nao mudou nada na tela. Com produto novo,
// tudo nascia limpo e nenhuma das dezenas de identidades daquele dia voltava a assombrar.
//
// Terminado o teste, VOLTAMOS a identidade original: o preco de manter a nova era todo jogo pedir
// para remapear o volante, e isso e caro demais para pagar por um andaime.
//
// SE PRECISAR REPETIR: mude o PRODUTO, nao o nome — e lembre de mover o app junto
// (DriveLab.Core/Protocol/BaseDeviceIdentity.cs). Se as duas pontas divergirem, o Studio deixa de
// encontrar a base.
//
// O numero de serie continua vindo do identificador do processador (ver drvlab_serial_do_mcu), o
// que mantem a base distinguivel de outra igual na mesma maquina.
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
#define DRVLAB_SO_JOYSTICK 0
#if DRVLAB_SO_JOYSTICK
static uint8_t s_hid_report_desc[sizeof(ffb_hid_report_desc)];
#else
static uint8_t s_hid_report_desc[sizeof(ffb_hid_report_desc) + sizeof(a0_hid_report_desc)];
#endif
// O comprimento declarado e os bytes entregues saem do MESMO sizeof: divergir aqui quebra a
// enumeracao inteira.
#define HID_REPORT_DESC_LEN (sizeof(s_hid_report_desc))
static uint8_t s_hid_desc_ready = 0;

// ============================================================================================
// TESTE: UMA COLECAO SO — sem o canal do app dentro do descritor
// ============================================================================================
// O descritor sempre teve DUAS colecoes de topo: a do joystick (com os efeitos de forca) e a do
// canal do app. Uma colecao de topo e, para o Windows, um CONTROLADOR — por isso o jogo passou a
// listar duas "DriveLab Base", uma com o volante e outra sem nada.
//
// A implementacao que roda neste mesmo hardware nao tem essa segunda colecao: ela leva a telemetria
// dentro do proprio relatorio do joystick. Nunca testamos assim, e e a unica diferenca estrutural
// que sobrou depois de conferir que TODO o caminho de forca esta identico ao firmware que
// funcionava (descritor de efeitos, gerenciador, leitor de pacotes e estado — 20/08/2026).
//
// Com 1, o app perde o canal dele enquanto o teste durar. E teste, nao decisao: se o jogo voltar a
// dar forca, a solucao definitiva e mover a telemetria para dentro do relatorio do joystick, e nao
// simplesmente ficar sem o canal do app.
void usb_hid_report_desc_build(void) {   // chamar ANTES do tud_init()
    memcpy(s_hid_report_desc, ffb_hid_report_desc, sizeof(ffb_hid_report_desc));
#if !DRVLAB_SO_JOYSTICK
    memcpy(s_hid_report_desc + sizeof(ffb_hid_report_desc), a0_hid_report_desc, sizeof(a0_hid_report_desc));
#endif
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
    "0001",                       // 3: Serial — SUBSTITUIDO em tempo de execucao pelo ID do MCU
                                  //    (ver drvlab_serial_do_mcu). Fica aqui so como reserva para
                                  //    o caso improvavel de o ID vir zerado.
    "DriveLab Base CDC",          // 4: CDC interface
    "DriveLab Base FFB"           // 5: HID interface
};
static uint16_t desc_str_buf[32];

// ─────────────────────────────────────────────────────────────────────────────
// NUMERO DE SERIE = ID UNICO DO MCU.
//
// O STM32F4 traz um identificador de 96 bits gravado em silicio na fabrica, em 0x1FFF7A10. Usa-lo
// como numero de serie da um valor ESTAVEL para a mesma placa e DIFERENTE entre placas.
//
// ⚠️ POR QUE "DIFERENTE ENTRE PLACAS" IMPORTA: o serial anterior era "0001", igual em todo mundo.
// Estavel ele ja era — entao trocar NAO conserta identidade instavel no jogo, e nao foi por isso
// que fizemos. O ganho e outro: com serial repetido, duas bases no mesmo PC colidem, e o Windows
// nao consegue guardar ajuste por placa. Com o ID do MCU, cada uma tem a sua identidade.
//
// ⚠️ EFEITO COLATERAL CONHECIDO: para o Windows isto e um dispositivo NOVO na primeira vez. Quem
// ja tinha a base mapeada num jogo pode precisar remapear uma vez. Acontece so na troca.
// ─────────────────────────────────────────────────────────────────────────────
#define DRVLAB_MCU_UID_ADDR 0x1FFF7A10u

static const char* drvlab_serial_do_mcu(void) {
    static char serial[25];
    static int  pronto = 0;
    if (!pronto) {
        const uint32_t* uid = (const uint32_t*)DRVLAB_MCU_UID_ADDR;
        const uint32_t w[3] = { uid[0], uid[1], uid[2] };
        // ID zerado nao existe em silicio bom; se acontecer, e sinal de leitura errada — devolvemos
        // NULL e o chamador cai na string de reserva em vez de anunciar "000000000000".
        if ((w[0] | w[1] | w[2]) == 0u) return NULL;
        static const char hex[] = "0123456789ABCDEF";
        int k = 0;
        for (int i = 0; i < 3; i++)
            for (int nib = 7; nib >= 0; nib--)
                serial[k++] = hex[(w[i] >> (nib * 4)) & 0xFu];
        serial[24] = 0;
        pronto = 1;
    }
    return serial;
}
uint16_t const* tud_descriptor_string_cb(uint8_t index, uint16_t langid) {
    (void)langid;
    if (index == 0) { desc_str_buf[0] = (TUSB_DESC_STRING << 8) | 4; desc_str_buf[1] = 0x0409; return desc_str_buf; }
    if (index >= sizeof(string_desc_arr)/sizeof(string_desc_arr[0])) return NULL;
    const char* str = string_desc_arr[index];
    if (index == 3) { const char* s = drvlab_serial_do_mcu(); if (s) str = s; }
    size_t chars = strlen(str); if (chars > 31) chars = 31;
    desc_str_buf[0] = (TUSB_DESC_STRING << 8) | (2*chars + 2);
    for (size_t i = 0; i < chars; i++) desc_str_buf[1+i] = (uint16_t)str[i];
    return desc_str_buf;
}
