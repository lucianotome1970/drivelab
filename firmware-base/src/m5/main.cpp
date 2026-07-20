// ============================================================================
//  DriveLab Firmware
//  main.cpp (m5) — Task 4 (M5 firmware-base — Montagem final): USB/A0 (Stage 0)
//  + FfbEngine (lib/brain) + DRV8301/SimpleFOC (Task 1-3) costurados no MESMO
//  firmware. Motor continua FISICAMENTE INCAPAZ de produzir torque — ver o
//  bloco ">>> MOTOR NÃO PODE SE MOVER <<<" logo abaixo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// >>> MOTOR NÃO PODE SE MOVER NESTE ARQUIVO. <<<
//   Esta é a "montagem" final do M5: USB/A0 (Stage 0, lib/base_usb) + o
//   canal FFB (lib/base_shared/ffb_report.h) + o "cérebro" (lib/brain/
//   ffb_engine.h) + a HAL SimpleFOC (Task 3, lib/base_motor/motor_hal.h) —
//   tudo no mesmo binário, mas a fiação até o motor físico continua cortada
//   de propósito:
//     * NUNCA chamamos driver.init() (não configura os timers/PWM/dead-time
//       de verdade — TIM1 nunca gera sinal nenhum nas fases).
//     * NUNCA chamamos motor.linkDriver()/linkSensor()/init()/initFOC()/
//       enable() — motor.init() SOZINHO já chamaria motor.enable() por
//       dentro (ver Arduino-FOC BLDCMotor::init(), Task 3 do M5), então nem
//       essa chamada aparece aqui.
//     * NUNCA chamamos encoder.init() — o encoder fica sem contagem alguma.
//     * O gate `g_calibrated` (const, sempre false neste arquivo) protege o
//       ÚNICO lugar que chamaria motor.loopFOC()/engine.step()/motor.move()/
//       motor.disable() — o bloco correspondente nunca EXECUTA. Isso importa
//       porque BLDCMotor::disable() desreferencia `driver` SEM checar null
//       (`driver->setPwm(...)`) e FOCMotor::move() desreferencia `sensor`
//       fora do modo open-loop — como driver/sensor nunca foram linkados
//       aqui, chamar qualquer um dos dois sem o gate seria um crash de
//       ponteiro nulo, não só "motor não se move". motor.loopFOC() sozinho
//       seria tecnicamente seguro mesmo sem o gate (checa `if(!enabled)
//       return` logo após um `if(sensor)` guardado — enabled=0 por padrão,
//       nunca setado porque enable() nunca é chamado), mas fica atrás do
//       mesmo gate por clareza: um único ponto decide "o motor pode se
//       mexer" em vez de espalhar a lógica de segurança.
//     * Só o DRV8301 é de fato configurado (drv.begin()+drv.configure()),
//       que é SPI puro (config de registro) — não gera PWM nas fases.
//     * FocBrake::setDuty() é NO-OP de propósito (ver motor_hal.cpp) — o
//       resistor de frenagem é um half-bridge (AUX_L/AUX_H, TIM2) cuja PWM
//       própria ainda não foi portada.
//   O Stage 1 (bancada, task futura) é quem vai flipar `g_calibrated` para
//   true DEPOIS de chamar driver.init()/motor.linkDriver()/linkSensor()/
//   init()/initFOC() na ordem certa — nada disso mora neste arquivo.
//
// USB/A0: reaproveita os módulos do M5 Stage 0 (lib/base_usb) exatamente
// como src/m05/main.cpp — dfuCheckAtBootOrJump() no topo do setup(),
// UsbBase::begin()+setReportCallbacks(), g_a0.begin()/serviceLoop()/
// dfuRequested()/saveRequested(). O handshake mínimo do PID HID (Create New
// Effect/Block Load/Pool) também é duplicado do m05 (nunca foi extraído
// para lib/base_usb — é específico do descritor FFB, não do canal A0) para
// que o host (DirectInput) realmente reconheça e crie efeitos de força,
// ainda que o motor não gire.

// IMPORTANTE — ordem de include: Adafruit_TinyUSB.h TEM que vir antes de
// Arduino.h/SimpleFOC.h (mesmo motivo documentado na Task 2 deste arquivo —
// ver histórico no git — e em lib/base_usb/usb_base.cpp): a macro `Serial`
// precisa já estar redefinida como `SerialTinyUSB` antes de Arduino.h/
// SimpleFOCDebug.h tentarem usá-la, senão vira auto-referência circular.
#include <Adafruit_TinyUSB.h>
#include <Arduino.h>
#include <SimpleFOC.h>

#include "ffb_hid_descriptor.h"
#include "ffb_report.h"
#include "a0_channel.h"
#include "fw_signature.h"
#include "sensors.h"
#include "dfu_jump.h"
#include "usb_base.h"

#include "drv8301.h"
#include "odrive_v36_pins.h"
#include "motor_hal.h"
#include "ffb_engine.h"
#include "apply_cfg.h"

// Taxa nominal do laço de torque -- usada por applyCfgToEngine() só para
// mapear settings dependentes de frequência (steps=auto do reconstructor,
// corte do biquad de outputFilterHz). AJUSTAR quando o laço de torque tiver
// taxa fixa (P1-4) -- hoje o motor nem chega a rodar (g_calibrated=false),
// então isto não afeta nenhum comportamento físico ainda.
static constexpr float kLoopHz = 1000.0f;

// ----------------------------------------------------------------------
// Layout do report de Input do RID_JOYSTICK — idêntico ao m05 (ver
// comentário lá): 8 bytes de botões (64 bits) + 16 bytes de eixos (8x
// int16). Servido só como prova de vida para o host enquanto o M5 não lê o
// encoder de verdade (que exige encoder.init(), fora de escopo aqui).
// ----------------------------------------------------------------------
struct JoystickInputReport
{
    uint8_t buttons[8];
    int16_t axes[8];
};
static_assert(sizeof(JoystickInputReport) == 24,
              "Payload do RID_JOYSTICK deve ter 24 bytes (8 botões + 8 eixos x16b)");

// ----------------------------------------------------------------------
// Estado mínimo do "pool" de efeitos PID — mesma contabilidade do m05 (ver
// comentário lá): nenhum efeito é armazenado/tocado de verdade, é só o
// handshake que o host (DirectInput) precisa para achar que o dispositivo
// tem onde alocar o efeito.
// ----------------------------------------------------------------------
static constexpr uint8_t kMaxEffectBlocks = 40;
static constexpr uint8_t kSimultaneousEffectsMax = 8;
static uint8_t g_nextEffectBlock = 1;
static uint8_t g_lastEffectBlock = 0;

// Canal A0 (config channel) — lib/base_usb/a0_channel.{h,cpp} (M5 Stage 0).
static A0Channel g_a0;

// ===================== SPI3 (compartilhado pelos dois DRV8301 no ODrive v3.6) =====================
static SPIClass spi3(kOdrivePinSpiMosi, kOdrivePinSpiMiso, kOdrivePinSpiSck);

// ===================== DRV8301 (gate driver do eixo M0) =====================
// Ganho do amp de shunt: G40 (40 V/V) — CONFIRMADO pela fonte de fábrica MKS
// v0.5.1 (o ODrive auto-seleciona esse ganho para o shunt de 500µΩ + faixa
// de 60A desta placa; era G20 na Task 2, placeholder conservador antigo).
static Drv8301 drv;

// ===================== Parâmetros do motor — AJUSTAR NA BANCADA =====================
static const int   POLE_PAIRS = 15;      // hoverboard in-wheel, ~15 pares — AJUSTAR na bancada
static const float ENC_CPR    = 4000.0f; // Omron E6B2-CWZ6C 1000 P/R × 4 (quadratura) = 4000 CPR
static const float SUPPLY_V   = 56.0f;   // MKS ODRIVE-S V3.6-S6V — variante 56V (NÃO 24V)

// ===================== SimpleFOC — construídos, NUNCA inicializados/ligados aqui =====================
// BLDCDriver6PWM(phA_h, phA_l, phB_h, phB_l, phC_h, phC_l, en) — 6-PWM (dead-time
// inserido pelo próprio timer TIM1/BDTR do STM32).
static BLDCDriver6PWM driver(kOdrivePinPhaseAH, kOdrivePinPhaseAL,
                              kOdrivePinPhaseBH, kOdrivePinPhaseBL,
                              kOdrivePinPhaseCH, kOdrivePinPhaseCL,
                              kOdrivePinEnGate);
static BLDCMotor motor(POLE_PAIRS);
static Encoder   encoder(kOdrivePinEncoderA, kOdrivePinEncoderB, ENC_CPR, kOdrivePinEncoderZ);

// ===================== HAL cérebro<->SimpleFOC (Task 3) — só CONSTRUÍDOS =====================
static drivelab::FocEncoder focEncoder(encoder);
static drivelab::FocCurrent focCurrent(Drv8301Gain::G40);
static drivelab::FocPower   focPower;
static drivelab::FocMotor   focMotor(motor);
static drivelab::FocBrake   focBrake;

// ===================== O "cérebro" FFB (lib/brain) =====================
static drivelab::FfbEngine engine;

// ----------------------------------------------------------------------
// g_calibrated — o ÚNICO interruptor que decide se o pipeline chega a tocar
// SimpleFOC (motor.loopFOC()/engine.step()/motor.move()/motor.disable()).
// SEMPRE false nesta task — não existe NENHUM caminho de código neste
// arquivo que o torne true. Fica para o Stage 1 (bancada): só depois de
// chamar driver.init() + motor.linkDriver()/linkSensor()/init()/initFOC()
// na ordem certa é que faz sentido (e é seguro) flipar isto. Ver o bloco
// ">>> MOTOR NÃO PODE SE MOVER <<<" no topo do arquivo para o porquê exato
// (null-deref em BLDCMotor::disable()/FOCMotor::move() sem driver/sensor
// linkados).
// ----------------------------------------------------------------------
static const bool g_calibrated = false;

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

// SET_REPORT Feature RID_PID_CREATE_NEW_EFFECT (0x11) — mesmo handshake do
// m05 (ver comentário lá para o layout de bytes).
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

// Callback de SET_REPORT — mesma dupla rota (endpoint OUT vs control
// transfer Feature) documentada em src/m05/main.cpp. Depois de A0Channel
// consumir o que é dela, os reports FFB restantes chegam aqui via
// ffb_parse_out(); FFB_SET_CONSTANT_FORCE alimenta o cérebro
// (engine.setGameForce()) — uma chamada PURA (só guarda o alvo de força em
// ForceReconstructor, nenhum hardware é tocado) que fica pronta para quando
// o Stage 1 ligar `g_calibrated`; até lá o valor é armazenado e nunca lido
// por engine.step() (que não roda, ver o gate acima).
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
        return;
    }

    // Canal A0 primeiro — mesma ordem/motivo do m05.
    if (g_a0.handleOutReport(buffer, bufsize))
    {
        return;
    }

    // Sub-projeto 2 (Parser de efeitos FFB, Task 4): roteia TODOS os OUT
    // reports PID (SetEffect/Envelope/Condition/Periodic/Constant/Ramp,
    // EffectOperation, BlockFree, DeviceControl) pro EffectManager, que
    // mantém o banco de slots e soma as forças em engine.step() (efeitos
    // Condition/Periodic/Ramp/etc.). nowMs() vem do clock ACUMULADO do
    // engine (m_nowMs, avançado por dt em step()) — não millis() direto —
    // pra compartilhar a mesma base de tempo usada por computeForce() e
    // manter phase/expiry coerentes. Constant force (0x05) também passa por
    // aqui, mas computeForce() pula FxType::Constant de propósito: essa
    // força já flui pelo ForceReconstructor logo abaixo (setGameForce), e
    // somar dos dois lados duplicaria a força constante do jogo.
    engine.effects.handleReport(buffer, bufsize, engine.nowMs());

    FfbOut o = ffb_parse_out(buffer, bufsize);
    switch (o.type)
    {
        case FFB_SET_CONSTANT_FORCE:
            SerialTinyUSB.printf("FFB const block=%u mag=%d\n", o.effectBlock, o.constantForce);
            // Só guarda o alvo (ForceReconstructor) — pura, sem hardware.
            // engine.step() (o que de fato leria isto e chamaria
            // motor.setTorque()) só roda atrás do gate `g_calibrated`
            // (sempre false aqui) — ver loop().
            engine.setGameForce(static_cast<float>(o.constantForce));
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
            break;
    }
}

// GET_REPORT Feature (Block Load / Pool) — mesmo handshake do m05.
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

// Ver o comentário completo em src/m05/main.cpp (has_out_endpoint=true, o
// descritor combinado FFB+A0 numa interface HID só) — idêntico aqui, mesma
// pilha (lib/base_usb/usb_base.{h,cpp}).
static Adafruit_USBD_HID *g_hid = nullptr;

void setup()
{
    // EnterDfu — PRIMEIRÍSSIMA coisa de setup(), antes de qualquer init de
    // USB/clock. Ver lib/base_usb/dfu_jump.{h,cpp}.
    dfuCheckAtBootOrJump();

    // Mantém fw_signature.h no binário final (--gc-sections) — mesmo truque
    // de inline asm documentado em src/m05/main.cpp (os dois caminhos mais
    // fracos que FALHARAM estão comentados lá; não repetido aqui).
    __asm__ __volatile__("" : : "r"(&fw_signature) : "memory");

    // ---- DRV8301: único hardware de fato configurado antes do USB (SPI
    // puro, sem PWM) ----
    // Só armazena voltage_power_supply — NÃO chama driver.init() (isso
    // configuraria TIM1/PWM de verdade; ver bloco de segurança no topo do
    // arquivo).
    driver.voltage_power_supply = SUPPLY_V;

    // BLDCDriver6PWM — tuning CONFIRMADO pela fonte de fábrica MKS v0.5.1
    // (ODrive TIM1, período 3500 @ 168MHz center-aligned ≈ 24kHz). Setados
    // no objeto AGORA (campos simples, sem tocar hardware) para já estarem
    // prontos quando o Stage 1 finalmente chamar driver.init() — nenhuma
    // PWM sai daqui enquanto driver.init() não for chamado.
    driver.pwm_frequency = 24000;
    // dead_zone como fração do período de PWM: o firmware oficial ODrive usa
    // 20 clocks de dead-time @ 168MHz = 119ns; 119ns / (1/24000s) ≈ 0.00286.
    // Usamos um valor um pouco MAIOR (mais dead-time, mais conservador contra
    // shoot-through) em vez do valor exato calculado.
    driver.dead_zone = 0.003f;

    spi3.begin();
    drv.begin(spi3, kOdrivePinCsM0, kOdrivePinEnGate, kOdrivePinNFault, Drv8301Gain::G40);
    const bool drvOk = drv.configure();

    // ---- Motor em modo torque por tensão — só CAMPOS, sem init/enable ----
    motor.torque_controller = TorqueControlType::voltage;
    motor.controller        = MotionControlType::torque;
    // Limite de tensão BEM conservador — só importa quando (Stage 1) motor
    // estiver de fato ligado; aqui é só o valor que já fica guardado no
    // objeto para esse dia.
    motor.voltage_limit = 2.0f;

    // ---- Config do "cérebro" (lib/brain) — 56V, CONSERVADORA ----
    engine.force.maxTorqueNm   = 2.5f;  // nominal placeholder — AJUSTAR na bancada
    engine.force.torqueLimitNm = 1.0f;  // teto DURO de segurança, baixo neste passo
    engine.startup.cfg.busMinV = 40.0f; // faixa 56V — abaixo disso, sem energia suficiente
    engine.startup.cfg.busMaxV = 60.0f; // acima disso, já perto do limite de sobretensão
    engine.guard.overVoltageV  = 60.0f; // corte duro de sobretensão (sistema 56V)
    engine.currentLimitA       = 1.5f;  // corte por sobrecorrente bem conservador
    engine.enableRequested     = false; // NUNCA setado true neste arquivo

    // ---- USB (HID FFB+A0 combinado + CDC) — lib/base_usb/usb_base.{h,cpp} ----
    UsbBase::setReportCallbacks(hid_get_report_callback, hid_set_report_callback);
    g_hid = &UsbBase::begin();

    // Canal A0: carrega os settings persistidos (ou semeia defaults).
    g_a0.begin();

    // Sub-projeto 1 (Feel ajustável ao vivo, Task 3): aplica os settings
    // carregados por cima do que acabou de ser hardcoded acima. Ordem
    // importa -- maxTorqueNm é constante de HARDWARE (fica setado antes),
    // torqueLimitNm/demais campos de "feel" são SETTINGS e vêm de
    // applyCfgToEngine() (sobrescreve o valor conservador fixo logo acima
    // por um derivado de c.maxTorqueLimit%). busMinV/busMaxV/overVoltageV
    // (guard/startup, safety-critical) NÃO são tocados por applyCfgToEngine
    // -- continuam valendo os hardcoded acima.
    applyCfgToEngine(g_a0.cfg(), engine, kLoopHz);

    SerialTinyUSB.printf("DriveLab M5 (Task 4) — DRV8301 configure()=%s ready=%s faulted=%s | motor OFF (sem init/enable/initFOC)\n",
                  drvOk ? "OK" : "FAIL",
                  drv.isReady() ? "true" : "false",
                  drv.faulted() ? "true" : "false");
}

void loop()
{
    // EnterDfu — mesma regra do m05 (fora do contexto do callback USB).
    if (g_a0.dfuRequested())
    {
        dfuRequestJump();
    }

    TinyUSBDevice.task();

    if (!TinyUSBDevice.mounted())
    {
        delay(2);
        return;
    }

    static bool bannerSent = false;
    if (!bannerSent)
    {
        bannerSent = true;
        SerialTinyUSB.printf("DriveLab M5 (Task 4) — USB/A0 + FFB->engine ativos | motor OFF (g_calibrated=false)\n");
    }

    // Canal A0: SaveSettings — escrita de fato na flash fora do callback USB.
    if (g_a0.saveRequested())
    {
        g_a0.clearSave();
        g_a0.save();
        SerialTinyUSB.printf("A0 saved\n");
    }

    uint32_t now = millis();

    static uint32_t lastSensor = 0;
    if (now - lastSensor >= 100)
    {
        lastSensor = now;
        sensorsSample();
    }

    // Resposta deferida do A0 (0x16) + telemetria periódica (0x21).
    g_a0.serviceLoop(now, &UsbBase::sendReport);

    // Sub-projeto 1 (Feel ajustável ao vivo, Task 3): ao vivo -- só reaplica
    // quando algum SETWRITE (0x14) de fato mexeu no BaseCfg (cfgDirty()),
    // nunca todo tick (applyCfgToEngine não é grátis: monta um Biquad
    // sempre que chamado).
    if (g_a0.cfgDirty())
    {
        applyCfgToEngine(g_a0.cfg(), engine, kLoopHz);
        g_a0.clearCfgDirty();
    }

    // ------------------------------------------------------------------
    // Gate do motor: a força só chegaria a virar torque se (a) o app armou
    // via SetForceEnabled (g_a0.forceEnabled()) E (b) o firmware já tivesse
    // sido calibrado na bancada (g_calibrated) — NENHUM código deste
    // arquivo torna g_calibrated true, então este bloco nunca EXECUTA aqui.
    // A verificação de forceEnabled() é feita mesmo assim (em vez de só
    // "if (false)") para documentar a condição completa que o Stage 1 vai
    // herdar quando ligar g_calibrated.
    // ------------------------------------------------------------------
    static uint32_t lastMicros = 0;
    if (g_calibrated && g_a0.forceEnabled())
    {
        // Nunca alcançado nesta task (g_calibrated é `const false`) — fica
        // pronto para o Stage 1 (bancada) depois de driver.init() +
        // motor.linkDriver()/linkSensor()/init()/initFOC().
        motor.loopFOC();
        const uint32_t nowUs = micros();
        const float dt = (nowUs - lastMicros) * 1e-6f;
        lastMicros = nowUs;
        engine.step(dt, focEncoder, focCurrent, focPower, focBrake, focMotor);
    }
    // (else: nada — motor.disable() NÃO é chamado aqui de propósito.
    // BLDCMotor::disable() desreferencia `driver` sem checar null; como
    // driver nunca foi linkado nesta task, chamar disable() seria um
    // null-pointer crash, não uma medida de segurança. O motor já está
    // fisicamente inerte porque driver.init()/motor.enable() nunca
    // rodaram — não precisa de "desligar" o que nunca foi ligado.)

    // Report de Input do RID_JOYSTICK — prova de vida, mesmo padrão do m05
    // (eixo X decorativo; o encoder real não está inicializado aqui).
    static uint32_t lastSend = 0;
    if (g_hid->ready() && (now - lastSend >= 10))
    {
        lastSend = now;

        JoystickInputReport report;
        memset(&report, 0, sizeof(report));
        report.axes[0] = (int16_t)(32767.0f * sinf(now / 1000.0f));

        UsbBase::sendReport(RID_JOYSTICK, (const uint8_t *)&report, sizeof(report));
    }
}
