// ============================================================================
//  DriveLab Firmware
//  main.cpp (m05) — M0.5 v2 Passo C: parser FFB ligado + log CDC + handshake
//  PID mínimo (Pool / Block Load / Create New Effect / Device Control).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

// DriveLab Firmware — M0.5 v2, Passo C (liga o "cano" FFB)
// Alvo: ODESC v4.2 (STM32F405). Framework: Arduino (STM32duino) + TinyUSB
// (Adafruit TinyUSB Library).
//
// Passo A provou a pilha TinyUSB isolada com um joystick mínimo. Passo B
// trocou o descritor mínimo pelo HID PID (Force Feedback) COMPLETO
// (ffb_hid_descriptor.h, Task 3) com um stub no-op no SET_REPORT — só provou
// que o descritor sai inteiro e o Windows mostra a aba Force Feedback.
//
// Este Passo C liga de fato o "cano": o SET_REPORT (endpoint OUT, reports
// como Set Constant Force / Effect Operation) é decodificado por
// ffb_report.{h,cpp} (Task 5) e logado via CDC. Para o host (DirectInput no
// Windows) realmente criar e tocar um efeito, ele antes precisa de um
// handshake PID mínimo por Feature report:
//   - SET_REPORT Feature RID_PID_CREATE_NEW_EFFECT (0x11): host pede um novo
//     efeito (Effect Type + Byte Count) -> nós alocamos um "Effect Block
//     Index" sequencial (1..40, MAX_EFFECTS do OpenFFBoard) e guardamos.
//   - GET_REPORT Feature RID_PID_BLOCK_LOAD (0x12): host pergunta o
//     resultado -> respondemos {Effect Block Index, Status=Success, RAM Pool
//     Available}.
//   - GET_REPORT Feature RID_PID_POOL (0x13): host pergunta a capacidade do
//     dispositivo -> respondemos {RAM Pool Size, Simultaneous Effects Max,
//     flags}.
//   - Device Control (RID_PID_DEVICE_CONTROL, 0x0C) chega pelo endpoint OUT
//     normal (é Output, não Feature) — só logamos.
// Os layouts de bytes desses três reports foram lidos direto da árvore de
// Main Items do ffb_hid_report_desc (ver comentários junto de cada handler
// abaixo) — eles espelham o PID Pool/Block Load/Create New Effect do
// OpenFFBoard (Ultrawipf/OpenFFBoard, Firmware/FFBoard/UserExtensions).
//
// SEGURANÇA: continua SEM motor / sem estágio de potência. Só decodifica e
// loga a força que o jogo manda — nenhum atuador é acionado.

#include <Arduino.h>
#include <Adafruit_TinyUSB.h>
#include "ffb_hid_descriptor.h"
#include "ffb_report.h"
#include "a0_hid_descriptor.h"

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
// Estado mínimo do "pool" de efeitos PID (só o necessário para o handshake
// -- nenhum efeito é de fato armazenado/tocado, é só contabilidade para o
// host achar que o dispositivo tem onde alocar o efeito).
//   - MAX_EFFECTS = 40 (0x28): mesmo limite do OpenFFBoard (ver
//     ffb_hid_descriptor.h / Logical Maximum do Effect Block Index nos
//     reports Create New Effect e Block Load).
//   - kSimultaneousEffectsMax: quantos efeitos "simultâneos" anunciamos no
//     PID Pool report — valor arbitrário conservador (não temos motor, é só
//     para o host não recusar o dispositivo por capacidade zero).
// ----------------------------------------------------------------------
static constexpr uint8_t kMaxEffectBlocks = 40;
static constexpr uint8_t kSimultaneousEffectsMax = 8;
static uint8_t g_nextEffectBlock = 1;    // último "Effect Block Index" alocado
static uint8_t g_lastEffectBlock = 0;    // 0 = nenhum alocado ainda

static const char *ffb_op_name(uint8_t op)
{
    switch (op)
    {
        case 1: return "start";
        case 2: return "start-solo";
        case 3: return "stop";
        default: return "unknown";
    }
}

// ----------------------------------------------------------------------
// SET_REPORT Feature RID_PID_CREATE_NEW_EFFECT (0x11): o host pede a
// criação de um novo efeito. Layout (sem o byte de Report ID, que a pilha
// TinyUSB já retira antes de chamar este callback para requests via control
// transfer — ver hid_device.c/HID_REQ_CONTROL_SET_REPORT):
//   buffer[0]      = Effect Type (array, 1..11 -> Constant Force/Ramp/...)
//   buffer[1..2]   = Byte Count (10 bits) + 6 bits de padding, little-endian
// Não alocamos memória de verdade — só devolvemos um Effect Block Index
// sequencial (1..kMaxEffectBlocks, dando a volta) para o host consultar via
// GET_REPORT Block Load logo em seguida.
// ----------------------------------------------------------------------
static void handle_create_new_effect(uint8_t const *buffer, uint16_t bufsize)
{
    uint8_t effectType = (bufsize >= 1) ? buffer[0] : 0;

    g_lastEffectBlock = g_nextEffectBlock;
    g_nextEffectBlock++;
    if (g_nextEffectBlock > kMaxEffectBlocks)
    {
        g_nextEffectBlock = 1;
    }

    SerialTinyUSB.printf("FFB create-effect type=%u -> block=%u\n", effectType, g_lastEffectBlock);
}

// ----------------------------------------------------------------------
// Callback de SET_REPORT: chega por dois caminhos distintos na
// Adafruit_USBD_HID / TinyUSB (ver hid_device.c):
//   1) Transfer real no endpoint OUT (Set Effect/Envelope/Condition/
//      Periodic/Constant Force/Ramp Force/Effect Operation/Block Free/
//      Device Control/Device Gain — todos "Output" no descritor):
//      report_type == HID_REPORT_TYPE_OUTPUT, report_id == 0 (não usado
//      pela pilha nesse caminho) e o Report ID de verdade vem como
//      buffer[0] -- exatamente o formato que ffb_parse_out espera.
//   2) Control transfer SET_REPORT clássico (usado pelo PID Create New
//      Effect, que é "Feature"): report_type == HID_REPORT_TYPE_FEATURE,
//      report_id == o Report ID de verdade (ex.: RID_PID_CREATE_NEW_EFFECT)
//      e o buffer NÃO inclui mais o Report ID (a pilha já descartou).
// ----------------------------------------------------------------------
static void hid_set_report_callback(uint8_t report_id,
                                     hid_report_type_t report_type,
                                     uint8_t const *buffer, uint16_t bufsize)
{
    if (report_type == HID_REPORT_TYPE_FEATURE)
    {
        if (report_id == RID_PID_CREATE_NEW_EFFECT)
        {
            handle_create_new_effect(buffer, bufsize);
        }
        // Outros Feature SET (nenhum outro no nosso descritor) -- ignorado.
        return;
    }

    // Caminho 1 (endpoint OUT): buffer[0] é o Report ID de verdade.
    FfbOut o = ffb_parse_out(buffer, bufsize);
    switch (o.type)
    {
        case FFB_SET_CONSTANT_FORCE:
            SerialTinyUSB.printf("FFB const block=%u mag=%d\n", o.effectBlock, o.constantForce);
            break;

        case FFB_EFFECT_OPERATION:
            SerialTinyUSB.printf("FFB effect-op block=%u op=%s\n", o.effectBlock, ffb_op_name(o.op));
            break;

        case FFB_DEVICE_CONTROL:
            SerialTinyUSB.printf("FFB device-control len=%u\n", bufsize);
            break;

        case FFB_SET_EFFECT:
        case FFB_BLOCK_LOAD:
        case FFB_UNKNOWN:
        default:
            // Passo C só precisa provar o "cano" de Constant Force + Effect
            // Operation; os demais reports são aceitos e ignorados (sem
            // travar/crashar) -- ver ffb_report.h.
            break;
    }
}

// ----------------------------------------------------------------------
// GET_REPORT Feature: o host lê os reports RID_PID_BLOCK_LOAD (0x12) e
// RID_PID_POOL (0x13) para decidir se cria/toca o efeito. `buffer` já vem
// sem o byte de Report ID (a pilha TinyUSB o antepõe sozinha antes deste
// retorno -- ver hid_device.c/HID_REQ_CONTROL_GET_REPORT); o retorno é o
// tamanho dos DADOS escritos (sem contar o Report ID).
// ----------------------------------------------------------------------
static uint16_t hid_get_report_callback(uint8_t report_id,
                                         hid_report_type_t report_type,
                                         uint8_t *buffer, uint16_t reqlen)
{
    if (report_type != HID_REPORT_TYPE_FEATURE)
    {
        return 0;
    }

    if (report_id == RID_PID_BLOCK_LOAD)
    {
        // Layout (Usage PID Block Load Report, ffb_hid_report_desc):
        //   buffer[0]    = Effect Block Index (1 byte)
        //   buffer[1]    = Block Load Status (array 1..3: 1=Success,
        //                  2=Full, 3=Error)
        //   buffer[2..3] = RAM Pool Available (uint16 little-endian)
        if (reqlen < 4)
        {
            return 0;
        }
        buffer[0] = g_lastEffectBlock;
        buffer[1] = 1; // Block Load Success
        uint16_t ramPoolAvailable = 0xFFFF;
        buffer[2] = (uint8_t)(ramPoolAvailable & 0xFF);
        buffer[3] = (uint8_t)(ramPoolAvailable >> 8);
        SerialTinyUSB.printf("FFB block-load -> block=%u status=success\n", g_lastEffectBlock);
        return 4;
    }

    if (report_id == RID_PID_POOL)
    {
        // Layout (Usage PID Pool, ffb_hid_report_desc):
        //   buffer[0..1] = RAM Pool Size (uint16 little-endian)
        //   buffer[2]    = Simultaneous Effects Max (1 byte)
        //   buffer[3]    = bit0 Device Managed Pool, bit1 Shared Parameter
        //                  Blocks, bits2..7 padding (tudo 0 aqui: pool não é
        //                  gerenciado pelo device, sem blocos compartilhados)
        if (reqlen < 4)
        {
            return 0;
        }
        uint16_t ramPoolSize = 0xFFFF;
        buffer[0] = (uint8_t)(ramPoolSize & 0xFF);
        buffer[1] = (uint8_t)(ramPoolSize >> 8);
        buffer[2] = kSimultaneousEffectsMax;
        buffer[3] = 0;
        SerialTinyUSB.printf("FFB pool -> size=%u simultaneous=%u\n", ramPoolSize, kSimultaneousEffectsMax);
        return 4;
    }

    return 0;
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

// IF1 — canal de configuração "A0" (vendor, usage-page 0xFF00): 2ª interface
// HID do composto, ao lado da IF0 (FFB, g_hid acima) e da IF2 (CDC, automática
// via Adafruit_USBD_Device::begin() — ver NOTA CDC no setup()). Descritor em
// a0_hid_descriptor.h. has_out_endpoint=true pelo mesmo motivo do g_hid: tem
// Output reports (A0_RID_CMD/DIRECT/SETWRITE/SETREAD) que só chegam pelo
// endpoint OUT dedicado. Este Task (2) só registra a interface — os
// callbacks de leitura/escrita (setReportCallback) ficam para a Task 3.
Adafruit_USBD_HID g_a0(a0_hid_report_desc, a0_hid_report_desc_len,
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
    // Callbacks de GET/SET_REPORT (ver comentários acima delas) — registrados
    // ANTES do begin() para já valerem assim que o host mandar/pedir algo.
    g_hid.setReportCallback(hid_get_report_callback, hid_set_report_callback);

    g_hid.begin();

    // IF1 (canal A0, vendor) — begin() ANTES do detach/attach de re-enum
    // abaixo, pelo mesmo motivo do g_hid: a Adafruit_TinyUSB_Arduino só
    // acrescenta a interface ao config descriptor quando begin() roda antes
    // da pilha ser (re)anunciada ao host. Sem callbacks ainda (Task 3) — só
    // registra a interface para o host enumerar.
    g_a0.begin();

    // Se a pilha já montou só com o CDC default antes do HID entrar, o host
    // não percebe a interface nova sem uma re-enumeração — força via
    // detach/attach (padrão oficial da lib).
    if (TinyUSBDevice.mounted())
    {
        TinyUSBDevice.detach();
        delay(10);
        TinyUSBDevice.attach();
    }

    // CDC de debug: "Serial" já é a CDC do TinyUSB neste core (STM32duino) —
    // tusb_config_stm32.h define "#define Serial SerialTinyUSB" e
    // Adafruit_USBD_CDC.h define "#define SerialTinyUSB Serial"; como as duas
    // macros se referenciam uma à outra, o pré-processador para a expansão
    // recursiva e ambos os nomes acabam resolvendo para o mesmo objeto
    // global `Adafruit_USBD_CDC SerialTinyUSB` (é o CDC registrado
    // automaticamente por Adafruit_USBD_Device::begin(), não uma UART).
    // Passo A deixou de usar Serial de propósito (só provava enumeração);
    // aqui é onde o Passo C liga o log de verdade.
    SerialTinyUSB.begin(115200);
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

    // Banner de boot via CDC — só uma vez, depois que o host monta o
    // dispositivo (antes disso os bytes escritos na CDC são descartados).
    static bool bannerSent = false;
    if (!bannerSent)
    {
        bannerSent = true;
        SerialTinyUSB.printf("DriveLab Base M0.5 Passo C — FFB pipe (parse + log CDC) ativo\n");
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
