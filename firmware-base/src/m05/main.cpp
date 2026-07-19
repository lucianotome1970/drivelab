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
#include <EEPROM.h>
#include "ffb_hid_descriptor.h"
#include "ffb_report.h"
#include "a0_hid_descriptor.h"
#include "base_cfg.h"
#include "fw_signature.h"
#include "sensors.h"

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
// Estado do canal A0 (config channel — Task 1/3): o modelo puro dos
// settings da base (base_cfg.h/.cpp) e o estado "leitura pendente" usado
// para responder A0_RID_SETVALUE (0x16) de forma DEFERIDA no loop() — a
// regra do endpoint único (mesmo achado de bancada do Passo C/Task 2: só
// dá pra ter um sendReport() "no ar" por vez no EP IN compartilhado por
// FFB+A0) impede responder direto de dentro do callback de SET_REPORT.
// Task 4 troca baseSeedDefaults() por um load da flash; por ora só semeia
// os defaults do schema (ver setup()).
// ----------------------------------------------------------------------
static BaseCfg g_baseCfg;
static bool g_pendingReadValue = false;  // true: loop() deve responder 0x16
static uint8_t g_pendingField = 0;       // fieldId pedido pelo último 0x15
static bool g_saveRequested = false;     // true: A0_RID_CMD pediu SaveSettings -> loop() grava na flash
static volatile bool g_dfuRequested = false; // true: A0_RID_CMD pediu EnterDfu -> loop() salta pro bootloader

// ----------------------------------------------------------------------
// EnterDfu, tentativa 2 (Task 2, redo pós-bancada) — "magic em RAM +
// reset de sistema + checagem no início do boot".
//
// A tentativa 1 (salto "ao vivo" pro bootloader de sistema de dentro do
// firmware rodando -- ver comentário antigo junto de jumpToBootloader()
// mais abaixo, mantido só como registro histórico) FALHOU na bancada: o
// host via o dispositivo desconectar (detach), mas o bootloader NUNCA
// re-enumerava (`dfu-util -l` não achava 0x0483:0xdf11). Causa raiz: o
// OTG_FS já tinha sido inicializado/usado pelo TinyUSB nesta mesma
// "vida" do chip -- HAL_RCC_DeInit()/HAL_DeInit() não bastam pra deixar
// o periférico USB num estado que o bootloader de sistema (que espera
// entrar como se fosse um power-on/reset limpo) reconhece. O bootloader
// da ST não foi desenhado pra ser saltado "ao vivo" de dentro de outro
// firmware com USB já ativo.
//
// A fix: em vez de saltar direto, a gente pede um NVIC_SystemReset() de
// verdade (reset de sistema completo -- reinicializa todos os
// periféricos, incluindo o OTG_FS, exatamente como um power-on) e só
// DEPOIS, já num boot novo e limpo, decide (bem no início do próximo
// setup(), ANTES de qualquer init de USB/clock) se deve pular pro
// bootloader. Isso precisa de um jeito de "lembrar" essa decisão
// atravessando o reset -- é pra isso que serve g_dfuMagic abaixo.
//
// Mecanismo de persistência escolhido: RAM não-inicializada (seção
// ".noinit"), NÃO registrador de backup do RTC. Motivo:
//   - Um NVIC_SystemReset() (reset de sistema via NVIC, o que a gente
//     usa aqui) NÃO limpa a SRAM -- só reinicializa os periféricos e o
//     core (ver PM0210 Rev 11, "1.3 System reset": um reset de sistema
//     "resets all registers... except FCLK/HCLK are configured... and
//     SRAM/registers of the... are unaffected" -- e mesmo sem citar a
//     RM ipsis litteris, é fato conhecido/documentado do Cortex-M/STM32
//     que RAM sobrevive a um reset que não seja power-on). O ÚNICO
//     "zeramento" que essa RAM sofreria é o loop do startup assembly
//     (Reset_Handler, startup_stm32f405xx.s) que copia .data da flash e
//     zera [_sbss, _ebss) -- e esse loop só toca os símbolos _sdata/
//     _edata/_sbss/_ebss, que vêm do LAYOUT do linker script
//     (ldscript.ld da variant F405RGT_F415RGT) -- uma seção com nome
//     PRÓPRIO (".noinit", não ".bss"/".bss*") cai fora desses ranges.
//   - Verificado: o ldscript.ld desta variant (STM32F4xx/
//     F405RGT_F415RGT/ldscript.ld) NÃO define uma seção ".noinit"
//     explícita, mas TAMBÉM não tem catch-all pra seções desconhecidas
//     -- o GNU ld trata ".noinit" como "orphan section" e a insere
//     sozinha numa região RAM alocável ("xrw") do MEMORY do script.
//     Confirmado via objdump -h no firmware.elf: o ld escolheu a região
//     CCMRAM (0x10000000, 64KB, região "xrw" separada da RAM principal
//     em 0x20000000 -- ainda SRAM on-chip normal do ponto de vista do
//     core Cortex-M4, só que num barramento dedicado, sem DMA -- não
//     importa aqui pois só o CPU lê/escreve g_dfuMagic), não a RAM
//     principal onde .data/.bss vivem -- ou seja, g_dfuMagic fica ainda
//     mais claramente FORA do range [_sbss, _ebss) que o Reset_Handler
//     zera (nem precisa checar endereço-a-endereço: é um bus/region
//     inteiramente diferente). Isso é o padrão comum/aceito em projetos
//     STM32duino/PlatformIO pra esse exato truque (magic de bootloader
//     sobrevivendo a reset) sem precisar editar o linker script -- e um
//     NVIC_SystemReset() não zera SRAM (nem a principal, nem a CCM), só
//     reinicializa periféricos/core.
//   - RTC->BKP0R funcionaria também (é o método "padrão-ouro" mesmo
//     através de um POWER-ON reset, que .noinit NÃO sobrevive), mas
//     exigiria ligar o clock de PWR + habilitar acesso ao domínio de
//     backup (__HAL_RCC_PWR_CLK_ENABLE + HAL_PWR_EnableBkUpAccess) e,
//     em alguns setups, o próprio RTC/LSE -- complexidade desnecessária
//     aqui: a gente só precisa sobreviver a um NVIC_SystemReset() (não
//     a um power-cycle), que é exatamente o caso em que .noinit já
//     basta. Fica registrado como fallback se .noinit se mostrar
//     não-confiável na bancada (não foi o caso -- ver relatório).
// ----------------------------------------------------------------------
static const uint32_t kDfuMagic = 0xB007DF00;
__attribute__((section(".noinit"))) static volatile uint32_t g_dfuMagic;

// Salto mínimo pro bootloader de sistema da ST -- chamado SÓ no início de
// setup() (ver checagem logo no topo de setup(), ANTES de qualquer init de
// USB/clock), ou seja, a partir de um NVIC_SystemReset() limpo, com o
// OTG_FS ainda no estado de reset (nunca foi tocado nesta "vida" do chip).
// Não precisa de flush/detach da USB (o OTG_FS nunca subiu neste boot), mas
// AINDA precisa de HAL_DeInit/HAL_RCC_DeInit: o core do STM32duino já
// reconfigurou o clock (HSE/PLL 168 MHz) antes do setup(), e o bootloader da
// ROM espera o clock no reset default (HSI) -- ver comentário abaixo.
static void jumpToBootloaderEarly()
{
    // O reset de sistema reinicia o NOSSO firmware, e o core do STM32duino já
    // reconfigura o clock (HSE/PLL 168 MHz) antes do setup() -- então neste
    // ponto o clock NÃO está no reset default. O bootloader por ROM (SW1/BOOT0)
    // roda no HSI. Precisamos voltar o clock pro estado de reset (HSI, HSE/PLL
    // off) e resetar os periféricos, senão a USB do bootloader não sobe (foi o
    // que falhou na bancada). HAL_RCC_DeInit usa SysTick p/ timeout -> chamar
    // ANTES de desligar SysTick/IRQ.
    HAL_DeInit();
    HAL_RCC_DeInit();

    __disable_irq();

    SysTick->CTRL = 0;
    SysTick->LOAD = 0;
    SysTick->VAL = 0;

    // Endereço do bootloader de sistema do STM32F405 (AN2606, boot via
    // system memory / BOOT0 alto): boot[0] = valor inicial do MSP,
    // boot[1] = endereço do reset handler.
    volatile uint32_t *boot = (volatile uint32_t *)0x1FFF0000;
    SCB->VTOR = 0x1FFF0000;
    __set_MSP(boot[0]);

    void (*blReset)(void) = (void (*)(void))boot[1];
    blReset();

    // Nunca deveria chegar aqui -- blReset() não retorna.
    while (1)
    {
    }
}

// ----------------------------------------------------------------------
// Persistência em flash (Task 4) — EEPROM emulada do STM32duino
// (Arduino_Core_STM32/libraries/EEPROM). No F405 (sem DATA_EEPROM real) essa
// lib emula a EEPROM na ÚLTIMA página de flash (E2END = FLASH_PAGE_SIZE-1 =
// 8KB-1 em stm32_eeprom.h) usando HAL flash por baixo -- API é a "clássica"
// Arduino AVR (EEPROM.get/put, sem begin()/commit(): cada EERef::operator=
// já escreve na flash na hora via eeprom_write_byte/eeprom_buffered_write_*,
// olhar EEPROMClass em EEPROM.h deste core). Isso é DIFERENTE do core RP2040
// usado no firmware-pedal (main.cpp ~L214-231), que exige EEPROM.begin(N) +
// EEPROM.commit() -- aqui não existem esses métodos na classe, então não são
// chamados (tentar chamar não compilaria).
// Layout: offset 0 = magic uint32 "DLB1" (0x444C4231, distinto do "DLP1" do
// pedal), offset 4 = BaseCfg inteiro (EEPROM.put/get cobre a struct como
// bytes crus -- mesmo padrão do pedal).
// ----------------------------------------------------------------------
static const uint32_t kBaseFlashMagic = 0x444C4231;  // "DLB1"
static const int kBaseFlashMagicAddr = 0;
static const int kBaseFlashCfgAddr = kBaseFlashMagicAddr + sizeof(kBaseFlashMagic);

static void saveBaseCfg()
{
    EEPROM.put(kBaseFlashMagicAddr, kBaseFlashMagic);
    EEPROM.put(kBaseFlashCfgAddr, g_baseCfg);
}

// Carrega g_baseCfg da flash se o magic bater. Retorna false (config
// inalterado) se a flash estiver vazia/corrompida -- setup() deve então
// chamar baseSeedDefaults().
static bool loadBaseCfg()
{
    uint32_t magic = 0;
    EEPROM.get(kBaseFlashMagicAddr, magic);
    if (magic != kBaseFlashMagic)
    {
        return false;
    }
    EEPROM.get(kBaseFlashCfgAddr, g_baseCfg);
    return true;
}

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

    // Canal A0 (config, Task 3): mesmo caminho 1 (endpoint OUT) do FFB acima
    // -- buffer[0] é o Report ID de verdade (confirmado pelo comentário da
    // função e por ffb_parse_out logo abaixo, que também lê buffer[0]).
    // A0_RID_SETWRITE/SETREAD/CMD/DIRECT são todos "Output" no descritor
    // (a0_hid_report_desc), então chegam por aqui, nunca pelo caminho
    // Feature. Layout (payload após o Report ID em buffer[0]):
    //   0x14 SETWRITE: buffer[1]=fieldId buffer[2]=index(0) buffer[3]=type
    //                  buffer[4..]=value LE (contrato SettingWrite do app).
    //   0x15 SETREAD:  buffer[1]=fieldId buffer[2]=index(0).
    //   0x22 CMD:      buffer[1]=cmd buffer[2]=arg.
    //   0x10 DIRECT:   ignorado por ora (só log) -- sem uso definido ainda.
    if (buffer[0] == A0_RID_SETWRITE)
    {
        if (bufsize >= 4)
        {
            uint8_t fieldId = buffer[1];
            uint8_t type = buffer[3];
            uint16_t valLen = bufsize - 4;
            baseWriteField(g_baseCfg, fieldId, type, &buffer[4], valLen);
            SerialTinyUSB.printf("A0 write field=%u type=%u len=%u\n", fieldId, type, valLen);
        }
        return;
    }

    if (buffer[0] == A0_RID_SETREAD)
    {
        if (bufsize >= 2)
        {
            g_pendingField = buffer[1];
            g_pendingReadValue = true;
            SerialTinyUSB.printf("A0 read field=%u\n", g_pendingField);
        }
        return;
    }

    if (buffer[0] == A0_RID_CMD)
    {
        if (bufsize >= 3)
        {
            uint8_t cmd = buffer[1];
            uint8_t arg = buffer[2];
            if (cmd == 2 /* SaveSettings */)
            {
                g_saveRequested = true;
                SerialTinyUSB.printf("A0 cmd=%u (SaveSettings) arg=%u -> g_saveRequested\n", cmd, arg);
            }
            else if (cmd == 4 /* EnterDfu */)
            {
                // Só sinaliza -- o salto de verdade acontece no loop() (ver
                // jumpToBootloader()), nunca aqui dentro do callback da
                // pilha USB (mesma regra do g_saveRequested acima: nada de
                // trabalho pesado/irreversível dentro do contexto de
                // interrupção/callback do TinyUSB).
                g_dfuRequested = true;
                SerialTinyUSB.printf("A0 EnterDfu\n");
            }
            else
            {
                SerialTinyUSB.printf("A0 cmd=%u arg=%u (sem handler ainda)\n", cmd, arg);
            }
        }
        return;
    }

    if (buffer[0] == A0_RID_DIRECT)
    {
        SerialTinyUSB.printf("A0 direct len=%u (ignorado por ora)\n", bufsize);
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
// interface HID (g_hid) — o buffer combinado é montado em setup() (ver
// g_combined_hid_report_desc) e atribuído a g_hid via setReportDescriptor()
// antes do g_hid.begin(). has_out_endpoint=true continua necessário: tanto o
// FFB (Set Effect/Envelope/.../Device Control/Device Gain) quanto o A0
// (A0_RID_CMD/DIRECT/SETWRITE/SETREAD) têm Output reports que só chegam pelo
// endpoint OUT dedicado.
Adafruit_USBD_HID g_hid(ffb_hid_report_desc, ffb_hid_report_desc_len,
                         HID_ITF_PROTOCOL_NONE, 4, /*has_out_endpoint=*/true);

// Buffer estático (precisa sobreviver ao runtime — tud_hid_descriptor_report_cb
// devolve o ponteiro guardado por g_hid a qualquer momento, inclusive bem
// depois do setup() retornar) com o Report Descriptor combinado: FFB
// (Joystick + PID/Force Feedback, Task 3) seguido do canal A0 (vendor, este
// Task). Preenchido em setup() antes de g_hid.begin() — ver comentário lá.
static uint8_t g_combined_hid_report_desc[ffb_hid_report_desc_len + a0_hid_report_desc_len];

void setup()
{
    // ------------------------------------------------------------------
    // Checagem EnterDfu (Task 2, redo) — TEM que ser a PRIMEIRÍSSIMA coisa
    // de setup(), antes de QUALQUER init de USB (TinyUSBDevice.begin()/
    // g_hid.begin() logo abaixo) ou até de clock. g_dfuMagic (RAM
    // ".noinit", ver comentário completo junto da declaração dela lá em
    // cima) só sobrevive ao NVIC_SystemReset() disparado por
    // jumpToBootloader() no loop() (ver mais abaixo) -- ela NÃO é limpa
    // pelo startup assembly (Reset_Handler só zera .bss). Se achar o
    // magic aqui, é porque acabamos de reiniciar de propósito pra entrar
    // no bootloader: consome o magic (senão um reset comum de novo cairia
    // de novo no bootloader, num loop) e salta -- com o OTG_FS ainda
    // intocado nesta "vida" do chip (nenhum TinyUSBDevice.begin() rodou
    // ainda), o bootloader de sistema entra limpo e re-enumera direito.
    // Nada antes disto no core (pré-setup(), ver Reset_Handler +
    // SystemInit) liga o OTG_FS -- SystemInit só configura clocks
    // (RCC/PLL) e flash wait-states, não periféricos USB.
    if (g_dfuMagic == kDfuMagic)
    {
        g_dfuMagic = 0;
        jumpToBootloaderEarly();
        // jumpToBootloaderEarly() não retorna.
    }

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
    // Monta o Report Descriptor combinado (FFB + A0, ver comentário junto de
    // g_combined_hid_report_desc) e o atribui a g_hid ANTES do begin() —
    // setReportDescriptor() só troca o ponteiro/tamanho guardados na
    // instância (Adafruit_USBD_HID.cpp), então a ordem aqui não afeta a
    // pilha diretamente, mas mantém a montagem e o begin() juntos e claros.
    memcpy(g_combined_hid_report_desc, ffb_hid_report_desc, ffb_hid_report_desc_len);
    memcpy(g_combined_hid_report_desc + ffb_hid_report_desc_len,
           a0_hid_report_desc, a0_hid_report_desc_len);
    g_hid.setReportDescriptor(g_combined_hid_report_desc, sizeof(g_combined_hid_report_desc));

    // Callbacks de GET/SET_REPORT (ver comentários acima delas) — registrados
    // ANTES do begin() para já valerem assim que o host mandar/pedir algo.
    // Task 2 (redo) só combina os descritores; os callbacks do canal A0
    // (Report IDs A0_RID_*) ficam para a Task 3 — hid_get/set_report_callback
    // continuam tratando só os RID_PID_*/RID_JOYSTICK do FFB.
    g_hid.setReportCallback(hid_get_report_callback, hid_set_report_callback);

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

    // Canal A0 (Task 4): tenta carregar os settings persistidos na flash
    // (ver loadBaseCfg()/saveBaseCfg() acima); se a flash estiver vazia ou
    // com o magic errado (1º boot, ou versão antiga do firmware), volta pros
    // defaults do schema (BaseSettingsSchema.cs via base_cfg.cpp).
    if (!loadBaseCfg())
    {
        baseSeedDefaults(g_baseCfg);
    }
}

// ----------------------------------------------------------------------
// EnterDfu (A0 cmd=4, Task 2 do plano "firmware update over USB") --
// PASSO 1 de 2 da tentativa 2 (magic + reset, ver comentário completo
// junto de g_dfuMagic lá em cima). Chamado do loop() (nunca do callback
// SET_REPORT -- mesma regra do g_saveRequested/g_pendingReadValue: nada
// de trabalho pesado/irreversível dentro do contexto do TinyUSB). Grava
// o magic em RAM .noinit e pede um reset de sistema de verdade
// (NVIC_SystemReset()) -- o salto de fato pro bootloader só acontece no
// PRÓXIMO boot, em jumpToBootloaderEarly() (chamada do início de
// setup(), ANTES de qualquer init de USB), a partir de um OTG_FS limpo.
// Não desmonta HAL/clock/USB aqui -- o reset de sistema já faz isso por
// completo (é justamente o que resolve o problema da tentativa 1: um
// HAL_RCC_DeInit()/HAL_DeInit() "manual" não deixa o OTG_FS tão limpo
// quanto um reset de verdade).
// ----------------------------------------------------------------------
static void jumpToBootloader()
{
    // Dá tempo do host/CDC drenar o log antes da gente sumir -- não é
    // estritamente necessário pro reset em si, mas evita truncar a última
    // linha de log ("A0 EnterDfu" etc.) na janela do monitor serial.
    SerialTinyUSB.flush();
    delay(10);

    g_dfuMagic = kDfuMagic;
    NVIC_SystemReset();

    // Nunca deveria chegar aqui -- NVIC_SystemReset() não retorna.
    while (1)
    {
    }
}

void loop()
{
    // EnterDfu (Task 2): flag setada no callback de SET_REPORT
    // (hid_set_report_callback, A0 cmd=4) -- o salto de verdade só
    // acontece aqui, fora do contexto de interrupção/callback USB (mesma
    // regra do g_saveRequested acima). jumpToBootloader() não retorna.
    if (g_dfuRequested)
    {
        g_dfuRequested = false;
        jumpToBootloader();
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

    // Canal A0 (Task 4): SaveSettings (0x22 cmd=2) chega no callback de
    // SET_REPORT (hid_set_report_callback), que só seta a flag -- a escrita
    // de fato na flash acontece aqui no loop(), fora do callback USB (mesma
    // lógica de "não fazer trabalho pesado dentro do callback da pilha" já
    // usada para a resposta deferida do 0x15/0x16 logo abaixo; aqui não há
    // sendReport() envolvido, mas ainda assim EEPROM.put() bloqueia por um
    // tempo -- melhor fora do contexto de interrupção/callback USB).
    if (g_saveRequested)
    {
        g_saveRequested = false;
        saveBaseCfg();
        SerialTinyUSB.printf("A0 saved\n");
    }

    // Canal A0 (Task 3): resposta DEFERIDA de A0_RID_SETREAD (0x15) via
    // A0_RID_SETVALUE (0x16) -- deferida porque o SET_REPORT do 0x15 chega
    // dentro do callback da pilha USB, onde não é seguro empilhar mais um
    // sendReport() (mesmo EP IN único, compartilhado com o Input do
    // RID_JOYSTICK abaixo). Prioridade sobre o Input do joystick nesta
    // iteração: só tenta o joystick se g_hid.ready() ainda estiver livre
    // depois deste envio (mesmo padrão do fix P0/HID EP do firmware-pedal/
    // wheel/handbrake -- ver MEMORY "Fix P0/HID EP").
    if (g_pendingReadValue && g_hid.ready())
    {
        g_pendingReadValue = false;

        uint8_t type = 0;
        uint8_t val[8] = {0};
        int n = baseReadField(g_baseCfg, g_pendingField, &type, val);

        // Payload do Input (SEM o Report ID -- g_hid.sendReport() antepõe
        // sozinho, igual ao RID_JOYSTICK abaixo): [0]=fieldId [1]=index(0)
        // [2]=type [3..]=value LE (contrato SettingValue do app).
        uint8_t payload[63] = {0};
        payload[0] = g_pendingField;
        payload[1] = 0;
        payload[2] = type;
        if (n > 0)
        {
            memcpy(&payload[3], val, n);
        }

        g_hid.sendReport(A0_RID_SETVALUE, payload, sizeof(payload));
        SerialTinyUSB.printf("A0 reply field=%u type=%u len=%d\n", g_pendingField, type, n);
    }

    // Report de Input do RID_JOYSTICK: eixo X variando devagar (prova que o
    // host VÊ o dispositivo se mexendo), demais campos zerados/centrados.
    // Passo B não decodifica FFB ainda — só serve o descritor inteiro e
    // mantém um Input válido fluindo.
    static uint32_t lastSend = 0;
    uint32_t now = millis();

    // Amostra os sensores por ADC a ~10 Hz num cache (a telemetria abaixo usa o
    // cache). Fora do caminho do FFB/USB — leituras leves, read-only.
    static uint32_t lastSensor = 0;
    if (now - lastSensor >= 100)
    {
        lastSensor = now;
        sensorsSample();
    }

    if (g_hid.ready() && (now - lastSend >= 10))
    {
        lastSend = now;

        JoystickInputReport report;
        memset(&report, 0, sizeof(report));
        report.axes[0] = (int16_t)(32767.0f * sinf(now / 1000.0f)); // X

        g_hid.sendReport(RID_JOYSTICK, &report, sizeof(report));
    }

    // Canal A0 (Task 5): telemetria periódica DeviceState (A0_RID_STATE /
    // 0x21) -- é assim que o app (HidBaseTransport) sabe que a base está
    // conectada e habilita o dashboard. Layout EXATO espelhado de
    // app/DriveLab.Core/Protocol/BaseState.cs (ToBytes/Parse):
    //   [0..3]   FirmwareVersion (ReleaseType, Major, Minor, Patch) -- 1
    //            byte cada, SEM little-endian (WriteTo grava campo a campo).
    //   [4]      flags (BaseFlags) -- 0 por ora (nenhuma flag definida ainda
    //            usada pelo firmware).
    //   [5..6]   Position (int16 LE)
    //   [7..8]   AngleDeciDeg (int16 LE)
    //   [9..10]  Torque (int16 LE)
    //   [11..12] MotorCurrentMa (int16 LE)
    //   [13]     FetTempC (sbyte)
    //   [14]     ErrorCode (byte)
    //   [15..16] BusVoltageMv (uint16 LE)
    //   [17]     MotorTempC (sbyte)
    //   [18]     McuTempC (sbyte)
    //   [19..62] reservado -- zerado.
    // M0.5 não tem motor/sensores ainda (M1): todos os campos de 5..18 ficam
    // placeholder 0 (posição/ângulo/torque/corrente/temperaturas/barramento
    // "zerados" em vez de lidos) -- só a versão de firmware e as flags/erro
    // (também 0) são preenchidos de verdade.
    // Prioridade: menor prioridade dos três sends do EP IN compartilhado
    // (0x16 deferido > 0x01 joystick > 0x21 aqui) -- só tenta se g_hid.ready()
    // ainda estiver livre depois dos dois envios acima nesta mesma iteração
    // (mesmo padrão "um report por janela de EP" do fix P0/HID EP).
    static uint32_t lastStateSend = 0;
    if (g_hid.ready() && (now - lastStateSend >= 15))
    {
        lastStateSend = now;

        uint8_t payload[63] = {0};
        payload[0] = 0;                       // FirmwareVersion.ReleaseType (0 = dev)
        payload[1] = DRVLAB_FW_VER_MAJOR;     // FONTE ÚNICA em fw_signature.h — bate com a assinatura do .bin
        payload[2] = DRVLAB_FW_VER_MINOR;
        payload[3] = DRVLAB_FW_VER_PATCH;
        payload[4] = 0; // flags
        // payload[5..12] = posição/ângulo/torque/corrente -- 0 até o M1 (motor).
        // payload[13] = FetTempC, [15..16] = BusVoltageMv -- adiados p/ M1 (pinos
        // do clone MKS divergem; ver sensors.h). Ficam 0 (placeholder honesto).
        payload[14] = 0;                            // error
        // payload[17] = MotorTempC -- 0 até M1 (thermistor de motor externo).
        payload[18] = (uint8_t)sensorMcuTempC();    // McuTempC (sbyte): sensor interno do F405

        g_hid.sendReport(A0_RID_STATE, payload, sizeof(payload));
    }
}
