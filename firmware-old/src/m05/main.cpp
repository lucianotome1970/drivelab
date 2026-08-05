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
#include "a0_channel.h"
#include "fw_signature.h"
#include "sensors.h"
#include "dfu_jump.h"
#include "usb_base.h"

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

// ----------------------------------------------------------------------
// Canal A0 (config channel) — extraído para lib/base_usb/a0_channel.{h,cpp}
// (M5 Stage 0, Task 2): framing de SET_REPORT (SETWRITE/SETREAD/CMD/
// DIRECT), resposta deferida de leitura (0x16), telemetria periódica
// DeviceState (0x21) e persistência do BaseCfg na flash — tudo dentro do
// módulo agora, reusável por um futuro firmware M5 sem duplicar a lógica.
// main.cpp só delega (ver hid_set_report_callback/setup/loop abaixo).
// ----------------------------------------------------------------------
static A0Channel g_a0;

// ----------------------------------------------------------------------
// EnterDfu (magic em RAM + reset de sistema + checagem no início do boot) —
// extraído para lib/base_usb/dfu_jump.{h,cpp} (M5 Stage 0, Task 3): ver
// dfuCheckAtBootOrJump()/dfuRequestJump() ali (histórico completo da
// tentativa 1 que falhou na bancada, motivo do RAM ".noinit" em vez de
// RTC->BKP0R, etc. — mantido lá, não duplicado aqui). main.cpp só chama
// dfuCheckAtBootOrJump() bem no topo de setup() e dfuRequestJump() do
// loop() quando g_a0.dfuRequested().
// ----------------------------------------------------------------------

// Persistência em flash (EEPROM emulada do STM32duino) do canal A0 agora
// vive em A0Channel::begin()/save() (lib/base_usb/a0_channel.cpp) — ver
// comentário lá para o layout (magic "DLB1" + BaseCfg) e as notas sobre a
// API EEPROM deste core (diferente do RP2040 do firmware-pedal).

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

    // Canal A0 (config, lib/base_usb/a0_channel.{h,cpp}): mesmo caminho 1
    // (endpoint OUT) do FFB acima -- buffer[0] é o Report ID de verdade
    // (confirmado pelo comentário da função e por ffb_parse_out logo abaixo,
    // que também lê buffer[0]). A0_RID_SETWRITE/SETREAD/CMD/DIRECT são todos
    // "Output" no descritor (a0_hid_report_desc), então chegam por aqui,
    // nunca pelo caminho Feature. handleOutReport() devolve true se
    // consumiu o report (era A0) -- nesse caso não seguimos pra
    // ffb_parse_out; o layout de bytes de cada Report ID está documentado
    // junto de A0Channel::handleOutReport (a0_channel.cpp).
    if (g_a0.handleOutReport(buffer, bufsize))
    {
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
// Achado de bancada (redo do Task 2): o OTG_FS do STM32F405 só tem ~3
// endpoints IN utilizáveis (CDC consome 2, FFB 1) — não sobra um 4º IN para
// uma 2ª interface HID. Por isso o canal A0 (vendor, usage-page 0xFF00, ver
// a0_hid_descriptor.h) NÃO é mais uma interface separada: seus reports são
// apensados ao final do Report Descriptor do FFB e servidos pela MESMA
// interface HID (a instância vive em lib/base_usb/usb_base.cpp) — o buffer
// combinado é montado em UsbBase::begin() e atribuído via setReportDescriptor()
// antes do g_hid.begin(). has_out_endpoint=true continua necessário: tanto o
// FFB (Set Effect/Envelope/.../Device Control/Device Gain) quanto o A0
// (A0_RID_CMD/DIRECT/SETWRITE/SETREAD) têm Output reports que só chegam pelo
// endpoint OUT dedicado. Ver lib/base_usb/usb_base.{h,cpp} (M5 Stage 0, Task 3).

// Referência ao Adafruit_USBD_HID pronto (devolvida por UsbBase::begin() em
// setup()), guardada para o g_hid.ready()/sendReport() do joystick no loop()
// — o report da A0Channel::serviceLoop() usa UsbBase::sendReport() direto.
static Adafruit_USBD_HID *g_hid = nullptr;

void setup()
{
    // ------------------------------------------------------------------
    // Checagem EnterDfu — TEM que ser a PRIMEIRÍSSIMA coisa de setup(),
    // antes de QUALQUER init de USB (UsbBase::begin() logo abaixo) ou até
    // de clock. Ver lib/base_usb/dfu_jump.{h,cpp} (M5 Stage 0, Task 3) para
    // o mecanismo completo (magic em RAM ".noinit" + histórico de bancada);
    // esta chamada não retorna se o magic estiver setado (acabamos de
    // reiniciar de propósito pra entrar no bootloader).
    dfuCheckAtBootOrJump();

    // Referência explícita à assinatura embutida (fw_signature.h). O
    // __attribute__((used)) no struct só garante que o COMPILADOR não
    // descarte o símbolo dentro da unidade de tradução — mas o LINKER roda
    // com --gc-sections (ver build do PlatformIO), que ainda derruba
    // qualquer seção sem relocação alcançável a partir do reset handler.
    // Duas tentativas mais fracas FALHARAM (comprovado via
    // `objdump -dr main.cpp.o`, sem nenhuma referência a fw_signature, e
    // `check_fw_signature.py` não achando a assinatura no .bin final):
    //   1. "(void)&fw_signature;" — o endereço é calculado e descartado sem
    //      side effect observável; o compilador nem emite a relocação.
    //   2. "volatile uint8_t x = fw_signature.magic[0];" — como
    //      fw_signature é `const` com valor conhecido em tempo de
    //      compilação, o otimizador faz constant folding e embute o
    //      literal 'D' (0x44) direto, sem NUNCA tocar a memória do struct
    //      — a leitura "volatile" protege a variável local, não a origem
    //      do valor.
    // O que funciona: inline asm com constraint "r" força o compilador a
    // materializar o ENDEREÇO de fw_signature num registrador (não dá pra
    // constant-fold um endereço de memória do mesmo jeito que um valor),
    // criando uma relocação real que o linker precisa resolver — e com
    // isso --gc-sections não pode descartar a seção .rodata da assinatura.
    __asm__ __volatile__("" : : "r"(&fw_signature) : "memory");

    // Setup USB completo (identidade VID/PID via build flags, descritor
    // combinado FFB+A0, init/re-enum, CDC) — extraído para
    // lib/base_usb/usb_base.{h,cpp} (M5 Stage 0, Task 3). Callbacks de
    // GET/SET_REPORT (ver comentários acima delas) registrados ANTES do
    // begin() para já valerem assim que o host mandar/pedir algo — mesma
    // ordem do monolito original.
    UsbBase::setReportCallbacks(hid_get_report_callback, hid_set_report_callback);
    g_hid = &UsbBase::begin();

    // Canal A0: tenta carregar os settings persistidos na flash (ou volta
    // pros defaults do schema se a flash estiver vazia/com magic errado —
    // 1º boot, ou versão antiga do firmware). Ver A0Channel::begin()
    // (lib/base_usb/a0_channel.cpp).
    g_a0.begin();
}

void loop()
{
    // EnterDfu: flag setada no callback de SET_REPORT (hid_set_report_callback
    // -> A0Channel::handleOutReport, A0 cmd=4) -- o salto de verdade só
    // acontece aqui, fora do contexto de interrupção/callback USB (mesma
    // regra do saveRequested() abaixo). dfuRequested() consome a flag (só
    // devolve true uma vez). dfuRequestJump() (lib/base_usb/dfu_jump.h) não
    // retorna.
    if (g_a0.dfuRequested())
    {
        dfuRequestJump();
    }

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

    // Canal A0: SaveSettings (0x22 cmd=2) chega no callback de SET_REPORT
    // (hid_set_report_callback -> A0Channel::handleOutReport), que só seta a
    // flag -- a escrita de fato na flash acontece aqui no loop(), fora do
    // callback USB (mesma lógica de "não fazer trabalho pesado dentro do
    // callback da pilha" já usada para a resposta deferida do 0x15/0x16
    // dentro de A0Channel::serviceLoop; aqui não há sendReport() envolvido,
    // mas ainda assim EEPROM.put() bloqueia por um tempo -- melhor fora do
    // contexto de interrupção/callback USB).
    if (g_a0.saveRequested())
    {
        g_a0.clearSave();
        g_a0.save();
        SerialTinyUSB.printf("A0 saved\n");
    }

    uint32_t now = millis();

    // Amostra os sensores por ADC a ~10 Hz num cache (a telemetria de dentro
    // de A0Channel::serviceLoop usa o cache via sensorMcuTempC()). Fora do
    // caminho do FFB/USB — leituras leves, read-only.
    static uint32_t lastSensor = 0;
    if (now - lastSensor >= 100)
    {
        lastSensor = now;
        sensorsSample();
    }

    // Canal A0: resposta DEFERIDA de A0_RID_SETREAD (0x15) via
    // A0_RID_SETVALUE (0x16) + telemetria periódica DeviceState (0x21) --
    // ambas dentro de A0Channel::serviceLoop (lib/base_usb/a0_channel.cpp),
    // que já respeita a prioridade "0x16 deferido antes de 0x21" e só envia
    // se UsbBase::sendReport() (lib/base_usb/usb_base.cpp) achar o endpoint
    // IN livre. Chamado ANTES do Input do joystick abaixo -- mesma
    // posição/prioridade que o 0x16 tinha no código original (é a resposta
    // de uma ação de UI, prioridade mais alta que o joystick decorativo);
    // só tenta o joystick se g_hid->ready() ainda estiver livre depois
    // (mesmo padrão do fix P0/HID EP do firmware-pedal/wheel/handbrake --
    // ver MEMORY "Fix P0/HID EP").
    g_a0.serviceLoop(now, &UsbBase::sendReport);

    // Report de Input do RID_JOYSTICK: eixo X variando devagar (prova que o
    // host VÊ o dispositivo se mexendo), demais campos zerados/centrados.
    // Passo B não decodifica FFB ainda — só serve o descritor inteiro e
    // mantém um Input válido fluindo.
    static uint32_t lastSend = 0;
    if (g_hid->ready() && (now - lastSend >= 10))
    {
        lastSend = now;

        JoystickInputReport report;
        memset(&report, 0, sizeof(report));
        report.axes[0] = (int16_t)(32767.0f * sinf(now / 1000.0f)); // X

        UsbBase::sendReport(RID_JOYSTICK, (const uint8_t *)&report, sizeof(report));
    }
}
