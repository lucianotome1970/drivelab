// ============================================================================
//  DriveLab Firmware
//  main.cpp (m5) — firmware-base "montagem final": USB/A0 (Stage 0) + FfbEngine
//  (lib/brain) + DRV8301/SimpleFOC (Task 1-3) + brake chopper — tudo no MESMO
//  binário. O motor É ACIONADO (FOC) por comando DELIBERADO de bancada e o
//  brake chopper está ATIVO — ver o "MODELO DE SEGURANÇA" abaixo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// >>> MODELO DE SEGURANÇA (atualizado 2026-08). <<<
//   [O header antigo dizia "MOTOR NÃO PODE SE MOVER" — era do snapshot Task 4 e
//    ficou OBSOLETO quando o acionamento do motor (M5/M6) e o brake chopper
//    entraram. Validado em bancada a sessão inteira.]
//   No BOOT o motor fica INERTE: driver.init()/motor.init()/enable() NÃO são
//   chamados, TIM1 não gera PWM nas fases, o encoder não conta — nada se move.
//   O motor só é ARMADO por comando DELIBERADO (operador de mão na tomada):
//     * cmd 5 (Calibrate) → stage1aStart(): chama driver.init()/motor.init(),
//       calibra o zero em MALHA ABERTA (velocidade aplicada — SEM o initFOC
//       bloqueante que pendurava a USB) → FOC_RUN → modos de FFB (mola / jogo
//       arg=97/96, cap de corrente 1A). AQUI o motor GIRA / produz torque —
//       validado (spring FFB, M5/M6, ACC/AMS2/EVO).
//     * cmd 8 (BrakeBench) / cmd 9 (BrakeAuto) → o brake chopper (FocBrake,
//       AUX_L/AUX_H no TIM2, gated DRVLAB_BRAKE_CHOPPER_HW) dumpa no resistor —
//       validado (sem shoot-through; o auto dispara sozinho no regen). NÃO é
//       mais NO-OP.
//   O gate `g_calibrated` (const false) ainda segura o ÚNICO caminho de FFB
//   AUTOMÁTICO (o engine.step que rodaria SEM o cmd 5) — protege contra o
//   null-deref de BLDCMotor::disable()/FOCMotor::move() com driver/sensor não
//   linkados. Ou seja: a força automática de PRODUÇÃO ainda é opt-in; na
//   bancada, o motor se move pelo caminho deliberado do cmd 5 acima.
//   Só o DRV8301 é configurado no setup (drv.begin()+drv.configure(), SPI puro).
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
#include <HardwareTimer.h>
#include <SimpleFOC.h>

#include "ffb_hid_descriptor.h"
#include "ffb_report.h"
#include "a0_channel.h"
#include "pid_state.h"
#include "fw_signature.h"
#include "sensors.h"
#include "sensor_convert.h"   // vbusDividerRatioFor() — divisor do VBUS por variante (24V/56V)
#include "dfu_jump.h"
#include "usb_base.h"
#include "dbg_ring.h"        // debug por ring buffer em RAM (SWD) — CDC removido

#include "drv8301.h"
#include "odrive_v36_pins.h"
#include "motor_hal.h"
#include "contactor_manager.h"
#include "power_button.h"    // soft-power por botão (Task 4, opt-in por power_button_enable)
#include "button_debounce.h" // debounce genérico de botão — reusado pelo OLED e pelo power button
#include "encoder_select.h"
#include "ffb_engine.h"
#include "apply_cfg.h"
#include "ffb_arm_guard.h"   // predicados puros de arme/desarme do modo FFB do jogo (M6)
#include "cogging_store.h"   // (de)serializacao da tabela de cogging p/ a flash
#include "foc_offset_cal.h"  // offset medio (media circular) + corte por corrente da calibracao (Task 1/2)
#include <EEPROM.h>          // EEPROM emulada (STM32duino) — blob da tabela de cogging

#ifdef DRVLAB_OLED
#include "ssd1306_i2c.h"     // visor OLED (I2C bit-bang no header GPIO)
#include "oled_status.h"     // composição das telas de status
#endif

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
static uint8_t g_lastEffectBlock = 0;  // bloco alocado no último Create New Effect (allocateBlock)

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
static const float ENC_CPR    = 4000.0f;  // DEFAULT (Omron E6B2-CWZ6C 1000 P/R × 4 = 4000 CPR). Usado no construtor do
                                          // Encoder e como fallback; o valor REAL vem do config (encoderCpr) em setup().
static float       g_encCpr   = ENC_CPR;  // CPR em uso (lido do config no boot + no cfgDirty). Trocar encoder = só mudar no app.
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

// ISRs de quadratura (A/B) do SimpleFOC — só LEITURA do encoder (contagem), NÃO ligam o motor. O índice Z
// NÃO é habilitado de propósito: o handleIndex() do SimpleFOC ancora full_rotations no índice, o que
// atrapalharia a contagem multi-turn de que o set-center depende (900°/1440° > 1 volta). Z fica p/ refino.
static void doEncoderA() { encoder.handleA(); }
static void doEncoderB() { encoder.handleB(); }

// Debug agora vai pro ring buffer em RAM (dbg_ring.h), lido por SWD — o CDC foi
// removido no redesign de compat com jogos (bDeviceClass 0xEF Misc/IAD do CDC fazia
// o AMS2 ignorar a placa; sem CDC → classe 0x00 limpa). Todos os `drivelab::dbgRingPrintf(...)`
// deste arquivo resolvem p/ drivelab::dbgRingPrintf. Ver [[drivelab-game-compat-a0]].

// ===================== HAL cérebro<->SimpleFOC (Task 3) — só CONSTRUÍDOS =====================
static drivelab::FocEncoder focEncoder(encoder);
static drivelab::FocCurrent focCurrent(Drv8301Gain::G40);
static drivelab::FocPower   focPower;

// Sensor de corrente PRÓPRIO. (O LowsideCurrentSense do SimpleFOC PENDURA o init nesta placa — a máquina de
// ADC injected/DMA + sync de timer conflita com nossos analogRead sequenciais. Ver bancada 2026-07-28.)
// Aqui: leitura dos shunts B/C (via FocCurrent → analogRead PC0/PC1, escala 0,0403 A/count) SINCRONIZADA ao
// PWM — espera o contador do TIM1 chegar perto do PICO (janela em que os FETs de baixo conduzem a corrente de
// fase), com timeout de segurança. Entregue ao foc_current por GenericCurrentSense. Tudo sequencial, sem
// injected/DMA/ISR → não pendura e não briga com a telemetria (que também é analogRead).
static bool g_csReady = false;

// Leitura RÁPIDA dos shunts direto no ADC2 (canais IN10=PC0=IB, IN11=PC1=IC). ~2µs/leitura (vs ~15µs do
// analogRead, que esfomeava a USB no loop do foc_current). ADC2 dedicado → não briga com o ADC1 dos analogRead
// de telemetria. Escala 0,0403 A/count; offset em counts medido em repouso.
static const float kAmpsPerCount = 3.3f / 4096.0f / (40.0f * 0.0005f);   // ~0,0403 A/count
static float g_off10 = 2048.0f, g_off11 = 2048.0f;                        // offset de repouso (counts)
static uint16_t g_lastJ1 = 2048, g_lastJ2 = 2048;                         // última amostra VÁLIDA (do vale) — DIR-gate (fallback)
// Stage 3: reusa a cadeia de captura injetada do SimpleFOC (já linkada) — a ISR JEOC dele preenche adc_val[idx][rank]
// COERENTEMENTE no vale (RepetitionCounter=1 → Update 1x/período no vale). idx do ADC2 = 1; rank1/2 = JDR1/JDR2.
extern ADC_HandleTypeDef hadc[];        // def. em Arduino-FOC .../stm32f4_hal.cpp (C++ linkage, símbolo não-mangled)
extern uint32_t          adc_val[5][4]; // def. em Arduino-FOC .../stm32f4_mcu.cpp
static volatile bool     g_jeocArmed  = false;  // true = captura JEOC acesa → genCurrentRead lê adc_val (coerente)
static volatile uint32_t g_jeocReject = 0;      // DIAG: amostras JEOC seguradas por sanidade (glitch >20A)
static volatile bool     g_fastPiActive = false; // Stage 3b: true = a ISR TIM6 roda o current loop (loopFOC) — só nas fases FECHADAS
static const float       kAngLagS = 0.0003f;     // compensação do atraso do ângulo cacheado (Option B, ~loop lento/2) — prevê a Park pela velocidade
// PI de corrente MANUAL na ISR (dt FIXO=125µs) — o PID do SimpleFOC usa _micros() p/ o dt, que não avança
// direito na ISR de 8kHz → integrador congela → Iq trava abaixo do setpoint (comanda 813mA, entrega 175mA).
static volatile float    g_curP = 0.18f, g_curI = 780.0f;   // ganhos (setados no arme de cP/cI)
static volatile float    g_integQ = 0.0f, g_integD = 0.0f;  // integradores do PI de corrente (reset no arme)
// FAIL-SAFE de runaway DENTRO da ISR (NÃO faminha como o corte do loop — que fez o usuário puxar o cabo 3x).
// A ISR lê o pulse_counter do encoder (atualizado por interrupt, independe do loop); a cada ~5ms mede o delta;
// se implica velocidade alta, ZERA o PWM e para o PI NA HORA — motor coasta, mesmo com o loop faminto.
static const float       kIsrRunawayRad = 0.1f;  // rad/5ms ≈ 20 rad/s (bate com o runawayCut do loop; girar a mola com a mão fica <0,06)
static const float       kMotorMaxC = 100.0f;    // sobretemp do MOTOR (pré-arme; NTC no PA5, -128 sem sensor → não trips)
static const float       kFetMaxC   = 90.0f;     // sobretemp dos FETs (NTC onboard PC5)
static const float       kIsrOverIqA = 2.5f;     // corte por SOBRECORRENTE na ISR (acima do cap 1A; pega corrente presa alta c/ vel~0 — o 13A do M6 que o corte de velocidade não vê)
static volatile bool     g_isrRunaway   = false; // a ISR setou o fail-safe → o loop desarma de vez

// SINCRONIZAÇÃO POR HARDWARE, SEM interrupção. ADC2 injected IN10/IN11 (PC0/PC1) disparado pelo TIM1_TRGO=Update.
// ⚠️ Em center-aligned o Update dispara no VALE (low-side ON = corrente real) E no PICO (all-high-side = ~0V):
// a discriminação vale/pico é feita no genCurrentRead pelo bit DIR (ver lá). O loop LÊ o JDR (pronto, ~0µs).
// Config COMPLETA e IDEMPOTENTE do ADC2 injected (TRGO=update do ODrive: o update SEMPRE fira, mesmo com MOE).
// Chamada A CADA leitura pra sobreviver a qualquer analogRead, que DE-INICIALIZA o ADC (limitação conhecida do
// SimpleFOC STM32 — analogRead init/deinit o ADC toda vez). ADON escrito JUNTO no CR2 (não pisca ligado→desligado).
static void adc2Config()
{
    RCC->AHB1ENR |= RCC_AHB1ENR_GPIOCEN;
    GPIOC->MODER |= (3u << 0) | (3u << 2);                        // PC0, PC1 = analog
    GPIOC->PUPDR &= ~((3u << 0) | (3u << 2));                     // sem pull
    RCC->APB2ENR |= RCC_APB2ENR_ADC2EN;
    ADC->CCR = (ADC->CCR & ~ADC_CCR_ADCPRE) | ADC_CCR_ADCPRE_0;   // ADCCLK = PCLK2/4 (<= 36MHz)
    ADC2->CR1 = ADC_CR1_SCAN;                                     // scan (2 inj), SEM interrupt
    ADC2->CR2 = (1u << 16) | (1u << 20) | ADC_CR2_ADON;           // JEXTSEL=TIM1_TRGO, JEXTEN=rising, ADON (junto)
    ADC2->SMPR1 = (3u << 0) | (3u << 3);                          // 56 ciclos ch10/11
    ADC2->JSQR = (1u << 20) | (11u << 15) | (10u << 10);          // JL=1: JSQ3=10→JDR1, JSQ4=11→JDR2
    TIM1->CR2 = (TIM1->CR2 & ~TIM_CR2_MMS) | (2u << 4);           // TRGO = update
}

static PhaseCurrent_s genCurrentRead()
{
    // CONFIG-ONCE (fix #3, pesquisa): NÃO reconfigurar o ADC2 por leitura. O ADC2 é configurado UMA vez em
    // genCurrentInit (na entrada da calibração). Os únicos analogRead que de-inicializam o ADC (temps + bus da
    // telemetria) estão BLOQUEADOS enquanto o current sense está ativo (g_csReady, gates ~1315/1483), então nada
    // de-inicializa o ADC2 durante a leitura de corrente. O re-config a cada read (removido) só CORROMPIA o JDR —
    // reescrever JSQR/CR2 async à conversão em curso é undefined (RM0090) → garbage mid-scale intermitente.
    // DIR-GATE (jeito ODrive, low_level.cpp:506-507 `counting_down = CR1 & TIM_CR1_DIR`): em PWM CENTER-ALIGNED
    // o TIM1 Update dispara DUAS vezes por período — no VALE (CNT≈0, todos os low-side ON = corrente de fase
    // REAL) e no PICO (CNT≈ARR, all-high-side = vetor zero, amp ~0V). SÓ o vale é corrente real. DIR=0
    // (up-counting = acabamos de sair do vale) → o JDR é a amostra boa: aceita e guarda; DIR=1 (down = saiu do
    // pico) → o JDR é ~0 legítimo (não corrente) → SEGURA a última do vale. Sem isto ~metade das amostras era o
    // pico ~0 → -80A fantasma → falso corte (bug confirmado na bancada). Ver docs/reference/current-sense-research.md.
    // ANTI-RACE do polling: como a gente LÊ o JDR (não é ISR pós-conversão como o ODrive), ler no DIR=0 logo após
    // o Update do vale pega o JDR AINDA com o valor do PICO (~0, conversão do vale não terminou). Aceita só quando
    // DIR=0 (janela do vale) E o JDR não é rail ~0 (>=500 counts) → senão é pico stale → SEGURA a última do vale.
    // Sem isto, um pico stale ~0 fazia o PI achar corrente=0 → sobre-excitava Uq → pico real (7,5A) → falso corte.
    uint32_t j1, j2;
    if (g_jeocArmed) {
        // STAGE 3: amostra do VALE capturada COERENTEMENTE pela ISR JEOC do SimpleFOC (adc_val[1][rank1/2] = os
        // JDR1/JDR2 do ADC2, lidos NO fim da conversão do vale). Coerente com a tensão (a ISR síncrona resolve o
        // que o DIR-gate/latch do polling compensava). GUARD DE SANIDADE: uma amostra que implica corrente
        // FISICAMENTE IMPOSSÍVEL (>kPhysMaxA, ~o dobro do máx de hardware do bus/R) é glitch (transição do PWM no
        // giro rápido / conversão espúria) → SEGURA a última boa (não alimenta o PI nem dispara o corte com
        // fantasma). Corrente real (≤~14A) passa e ainda corta em kCalMaxCurrentA=3A.
        const uint32_t v1 = adc_val[1][0], v2 = adc_val[1][1];
        const float maxDev = 20.0f / kAmpsPerCount;   // ±20A em counts (~496); >isso = glitch
        if (fabsf((float)v1 - g_off10) < maxDev && fabsf((float)v2 - g_off11) < maxDev) {
            g_lastJ1 = (uint16_t)v1; g_lastJ2 = (uint16_t)v2;
        } else {
            g_jeocReject++;                            // DIAG: conta glitches segurados
        }
        j1 = g_lastJ1; j2 = g_lastJ2;
    } else {
        // FALLBACK (offset init + diagnósticos ANTES do arme JEOC): polling do JDR com DIR-gate + anti-race ≥500.
        const uint32_t r1 = ADC2->JDR1, r2 = ADC2->JDR2;
        if (!(TIM1->CR1 & TIM_CR1_DIR) && r1 >= 500u && r2 >= 500u) { g_lastJ1 = (uint16_t)r1; g_lastJ2 = (uint16_t)r2; }
        j1 = g_lastJ1; j2 = g_lastJ2;
    }
    const float ib = ((float)j1 - g_off10) * kAmpsPerCount;          // IN10 = PC0 = IB
    const float ic = ((float)j2 - g_off11) * kAmpsPerCount;          // IN11 = PC1 = IC
    PhaseCurrent_s r; r.b = ib; r.c = ic; r.a = -(ib + ic);          // fase A por KCL
    return r;
}

static void genCurrentInit()
{
    adc2Config();
    delay(5);                                                    // deixa o TIM1 disparar algumas conversões
    float s10 = 0.0f, s11 = 0.0f; const int N = 400;             // offset de repouso (motor parado, sem corrente)
    for (int i = 0; i < N; ++i) { adc2Config(); s10 += ADC2->JDR1; s11 += ADC2->JDR2; delayMicroseconds(50); }
    g_off10 = s10 / (float)N; g_off11 = s11 / (float)N;
    drivelab::dbgRingPrintf("CS init: off IB=%d IC=%d | ADC2 SR=0x%lx JDR1=%lu JDR2=%lu MMS=%lu\n",
                         (int)g_off10, (int)g_off11, (unsigned long)ADC2->SR,
                         (unsigned long)ADC2->JDR1, (unsigned long)ADC2->JDR2, (unsigned long)((TIM1->CR2 >> 4) & 7u));
}

// Stage 3: acende a cadeia de captura injetada DORMENTE do SimpleFOC (ADC_IRQHandler → HAL_ADC_IRQHandler →
// HAL_ADCEx_InjectedConvCpltCallback → adc_val[1][]), sincronizada ao vale — SEM o LowsideCurrentSense.init()
// (que PENDURA nesta placa). O hang é no INIT do SimpleFOC, não na ISR; slots vazios do hadc[] são pulados
// (NP==0). Só populamos hadc[1].Instance=ADC2 + habilitamos o JEOC. Ver docs/reference/synced-current-loop-research.md.
static void focFastLoopArm()
{
    if (g_jeocArmed) return;
    hadc[1].Instance = ADC2;              // idx 1 = ADC2 (_adcToIndex); HAL_ADC_IRQHandler precisa do Instance
    ADC2->SR = ~(uint32_t)ADC_SR_JEOC;    // limpa JEOC pendente antes de habilitar
    ADC2->CR1 |= ADC_CR1_JEOCIE;          // habilita a interrupção de fim-de-conversão-injetada
    HAL_NVIC_SetPriority(ADC_IRQn, 1, 0); // acima da USB (a ISR do SimpleFOC é curta)
    HAL_NVIC_EnableIRQ(ADC_IRQn);
    g_jeocArmed = true;
    drivelab::dbgRingPrintf("FOC FAST: captura JEOC do SimpleFOC acesa (adc_val[1], sincronizada ao vale)\n");
}

static GenericCurrentSense currentSense(genCurrentRead, genCurrentInit);

// Tabela de cogging (por-motor): mora na flash em blob proprio (offset separado do BaseCfg), calibrada
// pelo criador na bancada e carregada no boot. ATENCAO bancada: a EEPROM emulada precisa ter >= ~700 bytes
// (BaseCfg em [0..~64] + blob de cogging em [128..~651]); confirmar EEPROM.length() na placa.
static drivelab::CoggingTable g_coggingTable;   // CoggingMap<128>
static bool g_hasCogging = false;
static constexpr int kCoggingFlashAddr = 128;   // offset separado do BaseCfg (magic+struct em [0..~64])

#ifdef DRVLAB_OLED
// Visor OLED SSD1306 no header GPIO da MKS: SDA=GPIO1(PA0), SCL=GPIO2(PA1), botão=GPIO3(PA2).
// Energia do 3.3V do header SPI (ABZ fica só pro encoder). Bit-bang I2C — pinos livres no firmware.
static constexpr int kOledPinSda    = PA0;
static constexpr int kOledPinScl    = PA1;
static constexpr int kOledPinButton = PA2;
static drivelab::Ssd1306I2c g_oled(kOledPinSda, kOledPinScl);
static drivelab::OledCanvas g_oledCanvas;
static drivelab::ButtonDebounce g_oledButton;
static int g_oledPage = 0;
#endif
static drivelab::FocMotor   focMotor(motor);
static drivelab::FocBrake   focBrake;

#ifdef DRVLAB_BRAKE_CHOPPER_HW
#include "brake_bench.h"
#include "brake_auto.h"
static bool     g_brakeBenchActive = false;   // true = modo bancada do freio ativo
static uint8_t  g_brakeDutyManual  = 0;        // duty manual em % (0..20)
static uint32_t g_brakeBenchT      = 0;        // timestamp do último cmd (auto-desarme)
static constexpr uint32_t kBrakeBenchTimeoutMs = 120000; // 120s sem novo cmd → desarma (folga p/ o ritmo humano de leitura/comando na bancada)
static drivelab::BrakeController g_brakeAutoCtrl;   // controlador auto (ffb_power.h) — cfg setada no arm
static bool     g_brakeAutoActive = false;          // true = modo auto-brake ativo
static uint32_t g_brakeAutoT      = 0;              // timestamp do último cmd (auto-desarme)
static constexpr float kBrakeAutoResOhm   = 1.8f;   // resistor medido (~1,8Ω)
static constexpr float kBrakeAutoMaxCurrA = 2.0f;   // cap de corrente (resistor 50W)
#endif

// ===================== O "cérebro" FFB (lib/brain) =====================
static drivelab::FfbEngine engine;

// Aplica/retira a tabela de cogging no engine conforme ela existe (g_hasCogging) E o setting coggingEnable.
// Definida aqui (depois de `engine`) porque referencia o engine e g_a0.
static void applyCoggingToEngine()
{
    engine.cogging = (g_hasCogging && g_a0.cfg().coggingEnable) ? &g_coggingTable : nullptr;
}

// ----------------------------------------------------------------------
// STAGE 1a v2 — PRIMEIRO GIRO em MALHA ABERTA (open-loop), NÃO-BLOQUEANTE e limitado no tempo.
// A v1 usava o initFOC BLOQUEANTE do SimpleFOC; quando o align (fraco demais) não convergiu, ele PENDUROU o
// loop -> a USB caiu (parecia "placa morta"). Aqui NÃO usamos initFOC: aplicamos VELOCIDADE em malha aberta
// por alguns segundos, com tensão BAIXA (corrente limitada). O loop NÃO bloqueia -> a telemetria continua
// (dá pra ver o encoder seguir e abortar) e para sozinho. Prova driver + comutação + direção do encoder SEM
// o risco do initFOC. Disparado pelo comando Calibrate (cmd 5), deliberado (operador de mão na tomada).
// EN_GATE: driver.init() só faz pinMode; o DRV8301 já está HIGH+configurado; motor.init() reafirma HIGH.
// ----------------------------------------------------------------------
static bool     g_olActive   = false; // giro open-loop em andamento
static uint32_t g_olUntilMs  = 0;     // instante (ms) em que para sozinho
static bool     g_motorReady = false; // teste open-loop concluído (Calibrated na telemetria)
static bool     g_motorFault = false; // abortado (bus baixo etc.) (Error na telemetria)
static float    g_uv         = 1.5f;  // tensão do vetor (V) do sweep manual — vem do arg do cmd 5
static float    g_elecAngle  = 0.0f;  // ângulo elétrico do sweep manual (rad)

// FOC MALHA FECHADA com CALIBRAÇÃO CONTÍNUA IDA-E-VOLTA (porte fiel do run_offset_calibration do ODrive —
// firmware de fábrica desta placa). Descoberta na bancada: passo ESTÁTICO não serve p/ este hub — o cogging
// prende o rotor na cova mais próxima, ele não alcança o ângulo comandado (2 voltas elétricas comandadas →
// rotor andou só ~20%), a média sai lixo → zero errado → malha fechada TRAVA. O initFOC do SimpleFOC também
// falha (bloqueia a USB e não aciona o driver aqui).
// SOLUÇÃO (ODrive): girar o campo LENTO e CONTÍNUO por N voltas elétricas p/ FRENTE e depois p/ TRÁS, arrastando
// o rotor pelas covas (momento vence o cogging), acumulando sin/cos de (dir*pp*mech − campo + _3PI_2) o tempo
// todo. A média ida+volta cancela o cogging E o atraso de carga (que troca de sinal entre ida e volta) → zero
// robusto. Checagem de sanidade (ODrive): se o rotor andou bem menos que o esperado, ESCORREGOU → aborta e pede
// mais tensão. Setup de torque comprovado (que já moveu o rotor na bancada).
//   LOCK 1s (assenta) → FWD 4s → BWD 4s → [COG opcional] → RUN malha fechada em VELOCIDADE por 6s.
// COGGING (cmd 7): depois da cal de zero, gira LENTO (kCogMeasV) gravando o Uq comandado por posição mecânica
// (= torque p/ manter a velocidade = cogging + atrito). CoggingCalibrator tira a média por bin, remove o DC
// (atrito) e inverte → mapa de compensação. No RUN, soma compensation(mech) ao feed_forward_voltage.q do
// SimpleFOC a cada tick → cancela o ripple. cmd 5 = cal + RUN (aplica cogging SE já medido); cmd 7 = cal +
// MEDE + RUN com compensação. A/B: rode cmd 5 (baseline) e depois cmd 7 (com cogging) e compare a ondulação.
enum FocPhase { FOC_IDLE, FOC_LOCK, FOC_FWD, FOC_BWD, FOC_VERIFY, FOC_COG, FOC_RUN };
static FocPhase g_focPhase  = FOC_IDLE;
static uint32_t g_focT      = 0;
static float    g_velTarget = 6.0f;   // velocidade alvo (rad/s) do RUN — vem do arg do cmd 5, sem reflash
static bool     g_skipVerifySpin = false;   // fit alto no scan => pula o giro-teste do VERIFY (finicky: runaway/TRAVOU) e vai direto pro modo

static ContactorManager g_contactor;   // proteção off-state (só ativa com cfg().softPowerEnable)
static bool g_contactorPinInit = false;
static bool g_driveWanted = false;   // operador pediu uma operação de motor (fecha o contator antes de g_focPhase sair de IDLE)

static PowerButton g_powerButton;   // soft-power por botão (só ativo com cfg().powerButtonEnable), holdMs=2000/cutDelayMs=200 (defaults)
static drivelab::ButtonDebounce g_powerBtnDebounce;   // debounce do botão de power (reusa o mesmo debouncer do botão do OLED)

// VERIFY: o zero da cal via scan em modo-tensão NÃO é repetível (cogging → stick-slip corrompe o scan). Depois
// de calcular o zero, dá um giro-teste; se NÃO girou (zero ruim → travou), recalibra automaticamente até girar.
static int          g_calRetry    = 0;
static const int    kCalMaxRetry  = 3;
static float        g_verStart    = 0.0f;   // getAngle no início do VERIFY

// SWEEP de posição (±900°) — teste de faixa como um volante. Modo ângulo, lento (velocity_limit baixo), gentil.
static bool  g_sweepMode   = false;         // arg==99 → sweep ±900° em vez de giro por velocidade
static float g_sweepCenter = 0.0f;          // getAngle no início do sweep (o "0" do volante)
static float g_sweepTarget = 0.0f;          // alvo atual (center ± amplitude)
static const float kSweepAmp = 15.708f;     // 900° em rad (2,5 voltas)

// FFB MOLA (arg=98) — o "hello world" do force feedback: o volante empurra de volta pro centro (mola) com
// amortecimento (estabiliza com o encoder laggy). Você gira na mão e SENTE a força. Torque via foc_current,
// capado em current_limit (0.5A gentil). Mola-amortecedor: current = -k*(ang-centro) - d*velocidade.
static bool  g_ffbMode    = false;          // arg==98 → modo FFB mola
static float g_ffbCenter  = 0.0f;           // getAngle do "centro" do volante
static float g_ffbSpring  = 0.30f;          // A/rad — força da mola (AO VIVO via springStrength; 0.30 = proporcional até ~95°)
static float g_ffbDamp    = 0.02f;          // A/(rad/s) — amortecimento (AO VIVO via damperStrength; baixo p/ o geartrain ruidoso)

// FFB DO JOGO (arg=97/96, M6) — arnês de comando que liga a força do JOGO (ACC via getForce/PID) ao motor
// real, com arme deliberado (canEnterGameFfb) e desarme por segurança (shouldDisarmGameFfb, Task 1). Torque
// via foc_current, capado em current_limit=1.0A (teto duro). arg=97 = feed do ACC (setGameForce já é chamado
// pelo ForceReconstructor no fluxo HID normal); arg=96 = sintético (injeta uma força pequena e CONHECIDA, só
// pra validar o caminho força→motor sem depender do jogo estar rodando).
static bool     g_gameFfbMode      = false;   // arg==97 (feed do ACC) ou arg==96 (força sintética conhecida)
static bool     g_gameFfbSynthetic = false;   // arg==96 → injeta força constante conhecida (passo (a))
static uint32_t g_gameFfbT         = 0;

// MONITOR de FFB do JOGO por ST-Link (openocd mdw): board USB→PC(jogo), ST-Link→Mac. Capturamos o que o jogo
// manda pra provar que o FFB chega (sem precisar do motor). [0]=nº reports FFB [1]=const force [2]=device gain
// [3]=último op/effect [4]=pos encoder (deci-deg) [5]=nº set-const-force.
volatile int32_t g_ffbMon[16] = {0};
// Instrumentação do freeze (2026-07-28): [6]=nº de GET_REPORT, [7]=último GET (type<<8|id),
// [8]=último retorno do GET (bytes; 0=STALL), [9]=nº de GET que deram STALL(0),
// [10]=último SET FEATURE report_id, [11]=nº de OUT reports consumidos pelo A0,
// [12]=nº de reports do JOYSTICK enviados, [13]=nº de vezes que o endpoint IN estava
// OCUPADO na hora de mandar o joystick (ready()==false), [14]=nº de PID State enviados,
// [15]=contador de iterações do loop() (prova que o loop roda e a velocidade).

// PID State report (Input, RID 0x02): o OpenFFBoard ENVIA este report de estado
// (HidFFB::sendStatusReport) após criar efeito / effect operation p/ confirmar ao
// host que os atuadores estão habilitados. Sem isso, o DirectInput do ACC TRAVA no
// carregamento da pista esperando a confirmação de estado do dispositivo FFB
// (firmware saudável, FFB fluindo, mas o jogo não avança). Marcamos "dirty" a cada
// OUT report de FFB e enviamos no loop (heartbeat ~30Hz), respeitando o endpoint IN.
static volatile bool g_pidStateDirty = false;

// Corte de RUNAWAY (segurança): se a velocidade disparar muito acima do esperado, desabilita NA HORA.
static bool runawayCut()
{
    if (fabsf(motor.shaft_velocity) > 20.0f) {   // rad/s — APERTADO p/ 20 (lição do runaway 2026-08-02: a 45 o motor esquentou demais antes de cortar). Girar a MOLA com a mão fica <10 rad/s; runaway pega cedo.
        engine.enableRequested = false;          // desliga o engine no runaway (M6)
        g_fastPiActive = false; motor.disable();   // Stage 3b: para a ISR ANTES de desarmar (não re-energiza)
        g_focPhase = FOC_IDLE; g_motorFault = true;
        drivelab::dbgRingPrintf("RUNAWAY! |vel|=%dmrad/s > 30 rad/s — DESABILITADO\n", (int)(motor.shaft_velocity*1000.0f));
        return true;
    }
    return false;
}

// Medição de cogging (motor-OFF gated; operador presente)
static drivelab::CoggingCalibrator<128> g_cogCal;
static bool           g_doCogMeasure = false;   // set pelo cmd 7 (mede) vs cmd 5 (só roda)
static const float    kCogMeasV   = 2.0f;       // rad/s da medição (lento → cogging domina o Uq)
static const uint32_t kCogMeasMs  = 12000;      // ~4 voltas mecânicas a 2 rad/s (2π/2 ≈ 3,14s/volta)
static const float    kCogFFClamp = 0.5f;       // teto do feed-forward de cogging (A, foc_current) — segurança

static const float    kCalV     = 3.0f;   // tensão de arrasto: 3V (~2,3A no ~1,3Ω) — FIRME p/ o rotor não escorregar no cogging (fit inconsistente a 2V). Ainda frio p/ scan curto; 6V puxava 4-6A → cozinhava. Ideal: medir R.
static const float    kCalOmega = 4.0f;   // rad/s ELÉTRICO da varredura (mais devagar → rastreio mais firme)
static const uint32_t kLockMs   = 1500;   // assentamento inicial (maior → parte de posição mais estável)
static const uint32_t kScanMs   = 6000;   // duração por sentido (~24 rad elec ≈ 3,8 voltas → média melhor)
static const float    kScanSpan = kCalOmega * (kScanMs / 1000.0f);   // rad elétricos por sentido (~20)
static drivelab::OffsetAccumulator g_offAcc;        // média circular do offset (Task 1) — substitui os g_sinP/... inline
static const float    kMinOffsetFit  = 0.5f;        // raio mínimo do phasor médio p/ aceitar o zero (0..1)
static const float    kCalMaxCurrentA = 3.0f;       // corte de segurança por corrente durante cal/verify (conservador; tunar na bancada)
static float    g_contStart=0.0f, g_contFwd=0.0f;   // getAngle (contínuo) no início e no fim do FWD

// Acumula uma amostra da calibração p/ as 2 hipóteses de direção (campo em ângulo elétrico `angField`).
// Frame preservado do código antigo: aP = pm - angField + _3PI_2 (dir=+1/CW). O OffsetAccumulator calcula
// aP = poleMechAngle - fieldAngle internamente, então passamos fieldAngle = angField - _3PI_2 pra bater.
static inline void calAccum(float angField)
{
    encoder.update();
    const float pm = (float)motor.pole_pairs * encoder.getMechanicalAngle();
    g_offAcc.add(pm, angField - _3PI_2);
}

// Corte de segurança por corrente durante a calibração/verify (Task 2), a partir de um Iq já medido.
// Compartilhado pelas duas variantes abaixo — abort limpo (desliga PWM, desabilita, sinaliza fault).
static bool calCurrentCutoffValue(float iq)
{
    if (drivelab::currentOverLimit(iq, kCalMaxCurrentA)) {
        drivelab::dbgRingPrintf("CAL CORTE: Iq=%dmA > teto %dmA — abortando\n",
                                 (int)(iq*1000.0f), (int)(kCalMaxCurrentA*1000.0f));
        motor.setPhaseVoltage(0,0,0); motor.disable();
        g_motorFault = true; g_focPhase = FOC_IDLE;
        return true;
    }
    return false;
}

// Corte de segurança por corrente a partir do ÂNGULO ELÉTRICO (LOCK/VERIFY, onde o tick ainda não mediu
// DQ por conta própria). Lê a corrente q via getFOCCurrents(angElectric) — não há loopFOC() rodando
// durante o scan (setPhaseVoltage direto), então o ângulo é passado explicitamente pelo chamador (o `ang`
// do próprio tick, ou motor.electrical_angle no VERIFY onde loopFOC() já roda).
static bool calCurrentCutoff(float angElectric)
{
    return calCurrentCutoffValue(currentSense.getFOCCurrents(angElectric).q);
}

// Diagnóstico do sensor de corrente (cmd 5 arg=100). Isolado — NÃO mexe no stage1a provado nem energiza
// o motor além do EN_GATE (PWM=0). Valida: cs.init() OK + offset calibrado + corrente em repouso ~0A +
// telemetria (bus/temp) ainda lendo (⇒ sem conflito de ADC). É o de-risco #1 do design.
static void currentSenseDiag(uint32_t nowMs, uint8_t arg)
{
    (void)nowMs; (void)arg;
    const float busV = focPower.busVoltage();
    if (busV < 8.0f) { drivelab::dbgRingPrintf("CS DIAG ABORT: bus baixo %dmV\n",(int)(busV*1000.0f)); return; }
    driver.voltage_power_supply = busV;
    if (!driver.initialized) { driver.dead_zone = 0.02f; driver.init(); }
    if (!drv.configure() || !driver.initialized) { drivelab::dbgRingPrintf("CS DIAG ABORT: drv\n"); return; }
    currentSense.linkDriver(&driver);
    if (!g_csReady) g_csReady = (currentSense.init() == 1);   // calibra offset (motor parado, PWM=0)

    if (arg == 101) {
        // Vetor de tensão conhecido (0,5V) em 4 ângulos → valida a leitura SOB corrente + o sync.
        // (O motor pode dar pequenos trancos alternando os ângulos — normal.)
        motor.linkDriver(&driver); motor.linkCurrentSense(&currentSense);
        motor.pole_pairs = g_a0.cfg().polePairs; motor.voltage_limit = 3.0f;
        motor.init(); motor.enable();
        adc2Config();                 // RE-ARMA o gatilho (motor.init/enable reconfigurou o TIM1)
        const float V = 0.5f;
        const float angs[4] = { 0.0f, _PI_2, _PI, _3PI_2 };
        for (int i=0;i<4;i++){
            motor.setPhaseVoltage(V, 0.0f, angs[i]); delay(200);
            if (i==0) drivelab::dbgRingPrintf("  dbg MMS=%lu SR=0x%lx JDR1=%lu JDR2=%lu ARR=%lu CCR4=%lu CCR1=%lu CCR2=%lu CCR3=%lu\n",
                (unsigned long)((TIM1->CR2>>4)&7u),(unsigned long)ADC2->SR,(unsigned long)ADC2->JDR1,(unsigned long)ADC2->JDR2,
                (unsigned long)TIM1->ARR,(unsigned long)TIM1->CCR4,(unsigned long)TIM1->CCR1,(unsigned long)TIM1->CCR2,(unsigned long)TIM1->CCR3);
            float sa=0,sb=0,sc=0; const int M=10;
            for(int k=0;k<M;k++){ PhaseCurrent_s p=currentSense.getPhaseCurrents(); sa+=p.a;sb+=p.b;sc+=p.c; delay(2); }
            DQCurrent_s dq = currentSense.getFOCCurrents(angs[i]);
            drivelab::dbgRingPrintf("  ang=%dmrad Ia=%dmA Ib=%dmA Ic=%dmA (KCL=%dmA) | Id=%dmA Iq=%dmA\n",
                (int)(angs[i]*1000),(int)(sa/M*1000),(int)(sb/M*1000),(int)(sc/M*1000),
                (int)((sa+sb+sc)/M*1000),(int)(dq.d*1000),(int)(dq.q*1000));
        }
        motor.setPhaseVoltage(0,0,0); motor.disable();
        drivelab::dbgRingPrintf("CS VETOR: fim\n");
        return;
    }

    float sa=0, sb=0, sc=0;                       // arg=100: média de 20 leituras em repouso (deve ~0A)
    for (int i=0;i<20;i++){ PhaseCurrent_s c = currentSense.getPhaseCurrents(); sa+=c.a; sb+=c.b; sc+=c.c; delay(2); }
    drivelab::dbgRingPrintf("CS DIAG: init=%d rest Ia=%dmA Ib=%dmA Ic=%dmA | busV=%dmV fetC=%d mcuC=%d\n",
        g_csReady?1:0, (int)(sa/20*1000), (int)(sb/20*1000), (int)(sc/20*1000),
        (int)(focPower.busVoltage()*1000), (int)lroundf(focPower.mosfetTempC()), sensorMcuTempC());
}

// (Re)inicia a varredura de calibração de zero (usado no início e em cada tentativa do VERIFY).
// Stage 3b (opção B): a parte do loopFOC que NÃO é corrente — atualiza o sensor + os ângulos (shaft/electrical)
// pro current-PI da ISR de 8kHz usar. Roda 1x por iteração nas fases FECHADAS do loop lento → o encoder é
// atualizado em UM único contexto (aqui), nunca também na ISR (evita a velocidade corrompida da reentrância).
static inline void slowFocSensorUpdate()
{
    if (motor.sensor) motor.sensor->update();
    motor.shaft_angle      = motor.shaftAngle();
    motor.shaft_velocity   = motor.shaftVelocity();
    motor.electrical_angle = motor.electricalAngle();
}

static void restartScan(uint32_t nowMs)
{
    g_offAcc = drivelab::OffsetAccumulator{};
    g_fastPiActive = false;   // Stage 3b: o scan é MALHA ABERTA (setPhaseVoltage direto) — a ISR NÃO roda loopFOC aqui
    motor.PID_current_q.reset(); motor.PID_current_d.reset();   // zera integrador: sem isso, retry herda estado do FWD/BWD/VERIFY anterior
    motor.controller = MotionControlType::torque;
    motor.feed_forward_voltage.q = 0.0f;
    motor.setPhaseVoltage(kCalV, 0.0f, _3PI_2);       // trava no início da varredura
    g_focPhase = FOC_LOCK; g_focT = nowMs;
    drivelab::dbgRingPrintf("CAL: lock + varredura ida/volta (V=%d.%dV, %d rad elec/sentido)...\n",
                         (int)kCalV, ((int)(kCalV*10))%10, (int)kScanSpan);
}

static void stage1aStart(uint32_t nowMs, uint8_t arg, bool doMeasure)
{
    g_fastPiActive = false;   // Stage 3b: garante que a ISR NÃO dirige durante o setup/scan (só liga no VERIFY)
    g_a0.setForceEnabled(true);   // reset: senão um cmd=6 arg=0 anterior (auto-desarme do monitor) persiste e o kill universal mata o re-arme na hora
    if (g_a0.cfg().softPowerEnable) g_driveWanted = true;   // sinaliza intenção → contator começa a fechar no loop

    const float busV = focPower.busVoltage();
    if (busV < 8.0f) { drivelab::dbgRingPrintf("FOC ABORT: bus baixo %dmV\n",(int)(busV*1000.0f)); g_driveWanted=false; g_motorFault=true; return; }

    // PRÉ-ARME: não energizar um motor JÁ QUENTE (sobretemp acumulada de teste/runaway). Leitura segura aqui
    // (antes do FOC/current-sense; o busVoltage acima já lê o ADC). Sem termistor do motor (motorTempC=-128) não
    // trips. NB: o corte DURANTE o FFB contínuo precisa de um read dedicado (ADC1) que não bata no current-sense
    // (ADC2) — follow-up quando o NTC do motor for soldado. Ver [[drivelab-motor-overtemp]].
    const float motC = focPower.motorTempC(), fetC = focPower.mosfetTempC();
    if (motC > kMotorMaxC || fetC > kFetMaxC) {
        drivelab::dbgRingPrintf("FOC ABORT: sobretemp pre-arme (motor=%dC fet=%dC) — esfriar antes\n", (int)motC, (int)fetC);
        g_driveWanted = false; g_motorFault = true; return;
    }

    // Contator (se ligado): não energizar o motor até as fases estarem conectadas (contator fechado).
    if (g_a0.cfg().softPowerEnable && !g_contactor.readyToDrive()) {
        // driveIntent vira true no loop (g_focPhase != IDLE || g_driveWanted); aqui só abortamos esta tentativa se ainda
        // não fechou — o operador re-dispara em ~50ms (o contator fecha em kCloseMs=30ms). g_driveWanted continua
        // true (setado acima) para o contator seguir fechando enquanto o operador re-dispara.
        drivelab::dbgRingPrintf("FOC: aguardando contator fechar (readyToDrive=0) — re-disparar\n");
        return;
    }
    g_driveWanted = false;   // a partir daqui g_focPhase != IDLE cobre o driveIntent

    g_doCogMeasure = doMeasure;
    g_sweepMode = (arg == 99);                     // arg=99 → sweep de posição ±900°
    g_ffbMode   = (arg == 98);                     // arg=98 → FFB mola
    g_gameFfbMode      = (arg == 97 || arg == 96); // modo FFB do jogo (97=ACC, 96=sintético)
    g_gameFfbSynthetic = (arg == 96);
    g_velTarget = (arg >= 1 && arg <= 30) ? (float)arg : 6.0f;   // so 1..30 = alvo de velocidade; modos especiais (98/99) usam 6
    driver.voltage_power_supply = busV;
    if (!driver.initialized) { driver.dead_zone = 0.02f; driver.init(); }
    const bool drvOk = drv.configure();
    if (!drvOk || !driver.initialized) { drivelab::dbgRingPrintf("FOC ABORT: drv=%d\n",drvOk?1:0); motor.disable(); g_driveWanted=false; g_motorFault=true; return; }
    motor.linkDriver(&driver);
    motor.linkSensor(&encoder);
    // Sensor de corrente (foc_current). init uma vez (calibra offset com PWM=0); reusa nas calibrações seguintes.
    currentSense.linkDriver(&driver);
    if (!g_csReady) g_csReady = (currentSense.init() == 1);
    motor.linkCurrentSense(&currentSense);
    motor.pole_pairs        = g_a0.cfg().polePairs;   // 15 (CONFIRMADO: 30 ímãs contados). O pp_est~16 da cal era ESCORREGÃO do rotor no sweep, não pp real.
    motor.init();
    motor.enable();                                   // EXPLÍCITO — sequência que PRODUZIU torque na bancada
    adc2Config();                                 // RE-ARMA o gatilho do ADC (motor.init/enable reconfigurou o TIM1)
    focFastLoopArm();                             // Stage 3a: acende a captura JEOC coerente (genCurrentRead lê adc_val[1])
    motor.controller        = MotionControlType::torque;
    motor.torque_controller = TorqueControlType::foc_current;   // <— era voltage: controle de CORRENTE (torque limpo)
    // ⚠️ SEGURANÇA (2026-07-28: um lurch quebrou o suporte do encoder). Limites BAIXÍSSIMOS até o tuning ficar
    // estável — 0.5A mal move o hub, quanto mais quebra algo. Subir só com cautela depois de suave.
    // voltage_limit ALTO = autoridade pro PI de corrente (NÃO é a segurança). A SEGURANÇA de torque é o
    // current_limit BAIXO (0.5A gentil). Com 2V o PI nao chegava nem a 1A e o motor nao girava.
    motor.voltage_limit     = 5.0f;
    motor.current_limit     = 1.0f;  // 1A: agora SEGURO (o PI R/L na ISR 8kHz não overshoota — v0.3.13). 0,5A era gentil demais p/ girar no VERIFY. Teto duro do torque de teste.
    motor.velocity_limit    = 12.0f;
    // PI de corrente — AO VIVO pelo config (currentP/currentI via app/HID) p/ tunar sem reflash. 0 = default.
    // Ganhos R/L CASADOS (IMC/jeito ODrive: P=ωc·L, I=ωc·R) → cancelam o polo do motor → resposta 1ª ordem SEM
    // overshoot. ωc=300 rad/s (conservador), L~0.3mH, R~1.3Ω. O 1.5/60 antigo era chute NÃO-casado (P→ωc5000,
    // I→ωc46) → windup → estourava (0,5A→6A). Só é estável com o PI RÁPIDO (ISR 8kHz, Stage 3b). AO VIVO via app.
    const float cP = (g_a0.cfg().currentP > 0.0f) ? g_a0.cfg().currentP : 0.18f;   // wc=600 · L=0,0003 (afiado 300→600: rastreia melhor o comando; PI rápido a 8kHz aguenta)
    const float cI = (g_a0.cfg().currentI > 0.0f) ? g_a0.cfg().currentI : 780.0f;  // wc=600 · R=1,3
    motor.PID_current_q.P = cP; motor.PID_current_q.I = cI; motor.PID_current_q.D = 0.0f;
    motor.PID_current_d.P = cP; motor.PID_current_d.I = cI; motor.PID_current_d.D = 0.0f;
    motor.PID_current_q.limit = motor.voltage_limit; motor.PID_current_d.limit = motor.voltage_limit;
    g_curP = cP; g_curI = cI; g_integQ = 0.0f; g_integD = 0.0f; g_isrRunaway = false;   // PI manual da ISR (dt fixo) + fail-safe: reset no arme
    motor.LPF_current_q.Tf = 0.005f; motor.LPF_current_d.Tf = 0.005f;
    // PID de velocidade: saída é CORRENTE (A) → limite = current_limit. Mais firme (menos drift do "acelerou no
    // meio") e rampa mais rápida (alvo em ~0.5s em vez de rastejar). Ainda gentil (torque capado em 0.5A).
    motor.PID_velocity.P = 0.10f; motor.PID_velocity.I = 0.30f; motor.PID_velocity.D = 0.0f;
    motor.PID_velocity.output_ramp = 8.0f;                   // rampa da corrente (A/s)
    motor.PID_velocity.limit = motor.current_limit;
    motor.LPF_velocity.Tf = 0.04f;   // filtro na velocidade (estimada é ruidosa)
    g_calRetry = 0;
    restartScan(nowMs);
}

static void stage1aTick(uint32_t nowMs)
{
    if (g_focPhase == FOC_IDLE) return;

    if (g_focPhase == FOC_LOCK) {
        motor.setPhaseVoltage(kCalV, 0.0f, _3PI_2);
        if (calCurrentCutoff(_3PI_2)) return;
        if ((int32_t)(nowMs - g_focT) >= (int32_t)kLockMs) {
            encoder.update(); g_contStart = encoder.getAngle();
            g_focT = nowMs; g_focPhase = FOC_FWD;
            drivelab::dbgRingPrintf("CAL: varrendo p/ FRENTE...\n");
        }
        return;
    }

    if (g_focPhase == FOC_FWD) {
        const float t   = (nowMs - g_focT) * 0.001f;
        const float ang = _3PI_2 + kCalOmega * t;         // campo avança devagar
        // TENSÃO ABERTA dimensionada por Lei de Ohm (jeito ODrive run_offset_calibration, encoder.cpp:197-203):
        // V ≈ calibration_current × R. NÃO é controle de corrente (o PI em ângulo forçado OSCILAVA — ganhos no
        // chute P=1.5/I=60 têm mismatch ~60-250x vs R/L) e NÃO é 6V (puxava 4-6A → I²R ~40W → cozinhava o DRV).
        // kCalV~2V ≈ 1,5A no nosso ~1,3Ω → I²R ~3W (frio). O offset médio (calAccum/computeOffset) cancela o
        // cogging; o corte (calCurrentCutoff, sensor consertado DIR-gate) é a rede de segurança. Ver
        // docs/reference/calibration-currentloop-research.md.
        motor.setPhaseVoltage(kCalV, 0.0f, ang);
        calAccum(ang);
        if (calCurrentCutoff(ang)) return;
        if ((int32_t)(nowMs - g_focT) >= (int32_t)kScanMs) {
            encoder.update(); g_contFwd = encoder.getAngle();
            g_focT = nowMs; g_focPhase = FOC_BWD;
            drivelab::dbgRingPrintf("CAL: FRENTE andou %dmrad (mech) -> varrendo p/ TRAS...\n",
                                 (int)((g_contFwd - g_contStart) * 1000.0f));
        }
        return;
    }

    if (g_focPhase == FOC_BWD) {
        const float t   = (nowMs - g_focT) * 0.001f;
        const float ang = _3PI_2 + kScanSpan - kCalOmega * t;   // campo volta
        motor.setPhaseVoltage(kCalV, 0.0f, ang);   // tensão aberta (ver FOC_FWD)
        calAccum(ang);
        if (calCurrentCutoff(ang)) return;
        if ((int32_t)(nowMs - g_focT) < (int32_t)kScanMs) return;

        // fim da varredura: direção + zero + sanidade
        motor.setPhaseVoltage(0.0f, 0.0f, 0.0f);
        const float dFwd     = g_contFwd - g_contStart;             // mech percorrido na ida (contínuo)
        const float expected = kScanSpan / (float)motor.pole_pairs; // mech esperado se seguiu 1:1
        if (fabsf(dFwd) < 0.4f * expected) {   // ODrive: resposta insuficiente = escorregou
            drivelab::dbgRingPrintf("CAL ABORT: rotor ESCORREGOU (andou %dmrad, esperado %dmrad) — cogging forte, subir tensao.\n",
                                 (int)(dFwd*1000.0f), (int)(expected*1000.0f));
            motor.disable(); g_motorFault=true; g_focPhase=FOC_IDLE; return;
        }
        // pp check (jeito SimpleFOC): mech percorrido × pp deve ≈ span elétrico (kScanSpan). Se divergir muito,
        // pole_pairs provavelmente está errado (dá "muita corrente, sem torque"). Só loga (não aborta).
        const float ppEst = fabsf(dFwd) > 1e-4f ? kScanSpan / fabsf(dFwd) : 0.0f;
        drivelab::dbgRingPrintf("CAL: dFwd=%dmrad pp_est=%d (cfg=%d) -> offset medio (Task 2)\n",
                             (int)(dFwd*1000.0f), (int)(ppEst+0.5f), (int)motor.pole_pairs);
        // ZERO pela MÉDIA CIRCULAR do offset (Task 1/2). A DIREÇÃO (CW/CCW) agora vem da FÍSICA do scan: o sinal
        // do dFwd (quanto o encoder REALMENTE andou enquanto o campo avançava) — NÃO mais do fit "mais apertado"
        // (que apontava direção errada no ruído → mola pro lado errado → RUNAWAY). O fit vira só medida de
        // qualidade; se baixo (escorregão/ruído), aborta. VALIDAR na bancada (motor frio): se a 1ª cal der runaway
        // no VERIFY, o mapeamento do sinal está invertido (trocar dFwd>=0 por dFwd<0).
        const int physDir = (dFwd >= 0.0f) ? +1 : -1;
        const drivelab::OffsetResult off = drivelab::computeOffset(g_offAcc, kMinOffsetFit, physDir);
        if (!off.ok) {
            drivelab::dbgRingPrintf("CAL ABORT: fit baixo (%d/1000) — escorregou/ruido\n",
                                    (int)(off.fit*1000.0f));
            motor.disable(); g_motorFault = true; g_focPhase = FOC_IDLE; return;
        }
        motor.sensor_direction = (off.direction > 0) ? Direction::CW : Direction::CCW;
        // O _3PI_2 já está embutido em aP/aM via o calAccum acima (add(pm, angField - _3PI_2) => aP = pm -
        // angField + _3PI_2), então off.zeroElectric JÁ é o zero elétrico no frame do SimpleFOC — não soma
        // _3PI_2 de novo aqui (bate com o antigo sweep-correlate: zero_electric_angle = atan2f(g_sinP, g_cosP),
        // sem termo extra). ⚠️ SINAL/DIREÇÃO NÃO VALIDADO EM BANCADA (sem fonte com limite de corrente ainda)
        // — se o VERIFY der runaway sistemático, é o 1º suspeito.
        motor.zero_electric_angle = off.zeroElectric;
        drivelab::dbgRingPrintf("CAL: ZERO(medio)=%dmrad dir=%s fit=%d/1000 -> MALHA FECHADA %d rad/s\n",
                                (int)(off.zeroElectric*1000.0f),
                                off.direction>0?"CW":"CCW", (int)(off.fit*1000.0f), (int)g_velTarget);
        motor.controller = MotionControlType::velocity;
        motor.feed_forward_voltage.q = 0.0f;
        encoder.update(); g_verStart = encoder.getAngle();
        slowFocSensorUpdate();   // Stage 3b: ângulo FRESCO antes de ligar a ISR (senão os 1ºs ticks usam angle stale)
        g_fastPiActive = true;   // Stage 3b: daqui pra frente (VERIFY→COG/RUN) o current loop roda na ISR de 8kHz
        g_skipVerifySpin = false;   // DESABILITADO (segurança): fit alto NÃO garante a DIREÇÃO do zero → mola empurrava pro lado errado → runaway. O VERIFY (giro-teste) PRECISA validar a direção. Fix real da direção = pela física do scan (sign(dFwd)), não pelo fit.
        g_focPhase = FOC_VERIFY; g_focT = nowMs;
        drivelab::dbgRingPrintf(g_skipVerifySpin ? "VERIFY: fit alto (%d/1000) -> PULANDO giro-teste, modo direto\n"
                                                 : "VERIFY: giro-teste 4 rad/s (fit %d/1000)...\n", (int)(off.fit*1000.0f));
        return;
    }

    // VERIFY — confirma que o zero gira; se travou, recalibra (o scan em modo-tensão nao e repetivel)
    if (g_focPhase == FOC_VERIFY) {
        float moved = 999.0f;   // caminho SKIP (fit alto): trata como zero bom SEM o giro-teste finicky
        if (!g_skipVerifySpin) {
            // runaway no VERIFY = zero ruim (geartrain deixa a cal não-repetível) → RECALIBRA em vez de abortar
            if (fabsf(motor.shaft_velocity) > 20.0f) {   // apertado 30→20 (segurança: pega a direção errada cedo, antes de esquentar)
                g_fastPiActive = false; motor.disable();   // Stage 3b: para a ISR ANTES de desarmar (não re-energiza)
                if (++g_calRetry <= kCalMaxRetry) {
                    drivelab::dbgRingPrintf("VERIFY runaway (zero ruim) — recalibrando %d/%d\n", g_calRetry, kCalMaxRetry);
                    motor.enable(); restartScan(nowMs);
                } else {
                    drivelab::dbgRingPrintf("VERIFY runaway apos %d tentativas — abortando\n", kCalMaxRetry);
                    g_motorFault = true; g_focPhase = FOC_IDLE;
                }
                return;
            }
            motor.feed_forward_voltage.q = 0.0f;
            slowFocSensorUpdate();   // Stage 3b: sensor+ângulos aqui; o current-PI roda na ISR de 8kHz
            if (calCurrentCutoff(motor.electrical_angle)) return;
            motor.move(4.0f);
            if ((int32_t)(nowMs - g_focT) < 1200) return;
            encoder.update();
            moved = fabsf(encoder.getAngle() - g_verStart);
        } else {
            motor.feed_forward_voltage.q = 0.0f;
            slowFocSensorUpdate();   // mantém o ângulo fresco pra ISR no caminho do skip (sem giro-teste)
        }
        if (moved > 1.5f) {                        // girou ~1/4 volta -> zero bom (ou SKIP por fit alto)
            drivelab::dbgRingPrintf("VERIFY OK: girou %dmrad (zero bom)\n", (int)(moved*1000.0f));
            g_motorReady = true; g_focT = nowMs;
            if (g_doCogMeasure) {
                g_cogCal.reset(); g_focPhase = FOC_COG;
                drivelab::dbgRingPrintf("COG: medindo cogging (giro lento %d rad/s por ~%ds)...\n",
                                     (int)kCogMeasV, (int)(kCogMeasMs/1000));
            } else if (g_sweepMode) {
                motor.controller     = MotionControlType::angle;   // controle de POSIÇÃO p/ o sweep
                motor.P_angle.P      = 5.0f;
                motor.velocity_limit = 3.0f;                       // sweep LENTO (3 rad/s)
                motor.P_angle.limit  = motor.velocity_limit;
                encoder.update(); g_sweepCenter = encoder.getAngle();
                g_sweepTarget = g_sweepCenter + kSweepAmp;         // 1º alvo: +900°
                g_focPhase = FOC_RUN;
                drivelab::dbgRingPrintf("-> SWEEP +/-900deg (angle mode, lento, 0.5A)\n");
            } else if (g_ffbMode) {
                motor.controller = MotionControlType::torque;      // torque direto (mola via foc_current)
                encoder.update(); g_ffbCenter = encoder.getAngle();
                const uint8_t ss = g_a0.cfg().springStrength, ds = g_a0.cfg().damperStrength;  // AO VIVO
                g_ffbSpring = (ss > 0 ? ss : 25) / 100.0f * 0.6f;   // default 25 → 0.15 A/rad (mola clara)
                g_ffbDamp   = (ds > 0 ? ds : 40) / 100.0f * 0.40f;  // default 40 → 0.16 A/(rad/s) (subido 0.08→0.16: absorve o snap-back violento do retorno que disparava o corte)
                g_focPhase = FOC_RUN;
                drivelab::dbgRingPrintf("-> FFB MOLA k=%dmA/rad d=%dmA/(rad/s) (gire e sinta!)\n",
                                     (int)(g_ffbSpring*1000.0f), (int)(g_ffbDamp*1000.0f));
            } else if (g_gameFfbMode && canEnterGameFfb(g_motorReady)) {
                motor.controller = MotionControlType::torque;   // torque via foc_current (idem mola)
                if (g_gameFfbSynthetic) engine.setGameForce(8000.0f);  // força constante FIRME p/ SENTIR (satura o cap de 1A no zero bom)
                // >>> BUG FIX (M6): SEM isto o engine.step() SEMPRE retorna 0 (startup.forceEnabled()==false).
                // Ligamos o engine aqui. Pulamos o align open-loop do brain (já FOC-calibramos por stage1a) e
                // usamos ramp curto (soft-start). O centro do FFB fica na posição atual (setCenterHere).
                focEncoder.setCenterHere();
                engine.startup.cfg.alignSeconds = 0.0f;
                engine.startup.cfg.rampSeconds  = 0.5f;
                engine.enableRequested = true;
                g_focPhase = FOC_RUN; g_gameFfbT = nowMs;
                drivelab::dbgRingPrintf("-> FFB DO JOGO (%s), teto 1A, engine ARMADO (enableRequested=1). Desarma em runaway/fault/USB-drop/app-off.\n",
                                     g_gameFfbSynthetic ? "SINTETICO" : "ACC");
            } else {
                g_focPhase = FOC_RUN;
                drivelab::dbgRingPrintf("-> RUN velocidade %d rad/s (cogging %s)\n",
                                     (int)g_velTarget, g_hasCogging ? "ON" : "OFF");
            }
        } else {                                   // travou -> zero ruim, recalibra
            g_calRetry++;
            if (g_calRetry <= kCalMaxRetry) {
                drivelab::dbgRingPrintf("VERIFY: TRAVOU (moveu so %dmrad) — recalibrando %d/%d\n",
                                     (int)(moved*1000.0f), g_calRetry, kCalMaxRetry);
                restartScan(nowMs);
            } else {
                drivelab::dbgRingPrintf("VERIFY: travou apos %d tentativas — abortando (cal instavel; fix real = sensor de corrente)\n", kCalMaxRetry);
                g_fastPiActive = false; motor.disable();   // Stage 3b: para a ISR ANTES de desarmar (não re-energiza)
                g_motorFault=true; g_focPhase=FOC_IDLE;
            }
        }
        return;
    }

    // COG — medição: gira lento e grava o Uq (torque) comandado por posição mecânica
    if (g_focPhase == FOC_COG) {
        if (runawayCut()) return;
        motor.feed_forward_current.q = 0.0f;   // NÃO compensa enquanto mede
        slowFocSensorUpdate();   // Stage 3b: sensor+ângulos aqui; current-PI na ISR
        motor.move(kCogMeasV);
        encoder.update();
        g_cogCal.addSample(encoder.getMechanicalAngle(), motor.current.q);   // Iq medido = torque p/ manter a vel = atrito+cogging
        static uint32_t lastLog = 0;
        if ((int32_t)(nowMs - lastLog) > 500) {
            lastLog = nowMs;
            drivelab::dbgRingPrintf("  COG vel=%dmrad/s mech=%dmrad Iq=%dmA\n",
                                 (int)(motor.shaft_velocity*1000.0f),
                                 (int)(encoder.getMechanicalAngle()*1000.0f),
                                 (int)(motor.current.q*1000.0f));
        }
        if ((int32_t)(nowMs - g_focT) >= (int32_t)kCogMeasMs) {
            g_cogCal.finish(g_coggingTable);
            g_hasCogging = true;
            float mn=1e9f, mx=-1e9f;                                          // pico-a-pico da compensação (A)
            for (int i=0;i<128;++i){ float v=g_coggingTable.table[i]; if(v<mn)mn=v; if(v>mx)mx=v; }
            drivelab::dbgRingPrintf("COG fim: mapa medido, comp pico-a-pico=%dmA -> RUN velocidade %d rad/s (cogging ON)\n",
                                 (int)((mx-mn)*1000.0f), (int)g_velTarget);
            g_focPhase = FOC_RUN; g_focT = nowMs;
        }
        return;
    }

    // FOC_RUN (giro por velocidade OU sweep de posição ±900°)
    if (g_isrRunaway) {   // a ISR de 8kHz já de-energizou (fail-safe que NÃO faminha); o loop desarma de vez
        g_fastPiActive = false;
        if (motor.driver != nullptr) motor.disable();
        g_driveWanted = false; g_focPhase = FOC_IDLE; g_motorFault = true; g_isrRunaway = false;
        drivelab::dbgRingPrintf("RUN: FAIL-SAFE da ISR (runaway pego pelo encoder) — desarmado\n");
        return;
    }
    if (runawayCut()) return;                                        // segurança: corta se a velocidade disparar
    // KILL SWITCH UNIVERSAL (cmd=6 arg=0 → forceEnabled=false): desarma QUALQUER modo do RUN (mola/sweep/veloc/
    // jogo). BUG 2026-08-02: a MOLA persistente NÃO tinha desarme por sw (só runawayCut) → o usuário teve que
    // PUXAR O CABO. forceEnabled default=true (arma normal); cmd=6 arg=0 → false → para na hora. Universal.
    if (!g_a0.forceEnabled()) {
        g_fastPiActive = false;
        if (motor.driver != nullptr) motor.disable();
        g_driveWanted = false; g_focPhase = FOC_IDLE;
        drivelab::dbgRingPrintf("RUN: KILL (cmd=6) — desarmado\n");
        return;
    }
    // Modo FFB do JOGO (M6): roda CONTÍNUO enquanto armado (como um volante de verdade) — NÃO tem
    // timeout fixo; para só por desarme (shouldDisarmGameFfb: USB-drop/app-off, checado no branch abaixo),
    // runawayCut (acima) ou watchdog do engine (força decai se o jogo parar de mandar). Os modos de bancada
    // (mola/sweep/velocidade) mantêm o timeout de segurança.
    const uint32_t runDur = g_sweepMode ? 24000u : (g_ffbMode ? 20000u : 6000u);
    // g_ffbMode (MOLA) agora é CONTÍNUO como o modo do jogo (pedido do usuário: mola persistente que sempre centra;
    // só desarma por cmd=6/runaway). Antes o timeout de 20s derrubava no meio do teste ("FOC: fim"/click).
    if (!g_gameFfbMode && !g_ffbMode && (int32_t)(nowMs - g_focT) > (int32_t)runDur) {
        motor.feed_forward_voltage.q = 0.0f;
        g_fastPiActive = false; motor.disable();   // Stage 3b: para a ISR ANTES de desarmar (não re-energiza)
        g_focPhase = FOC_IDLE;
        drivelab::dbgRingPrintf("FOC: fim\n");
        return;
    }
    slowFocSensorUpdate();   // Stage 3b: sensor+ângulos aqui; current-PI na ISR de 8kHz
    if (g_gameFfbMode) {
        // Desarme por segurança (predicado puro, Task 1): runaway JÁ foi checado por runawayCut() no topo;
        // aqui cobrimos USB-drop e app-off (fault do engine é tratado internamente por engine.step()).
        if (shouldDisarmGameFfb(/*runaway=*/false, /*fault=*/false,
                                TinyUSBDevice.mounted(), g_a0.forceEnabled())) {
            engine.enableRequested = false;   // desliga o engine (soft-start reseta na próxima)
            g_fastPiActive = false; motor.disable();   // Stage 3b: para a ISR ANTES de desarmar (não re-energiza)
            g_focPhase = FOC_IDLE;
            drivelab::dbgRingPrintf("FFB JOGO: desarmado (USB/app)\n");
            return;
        }
        // CORTE POR CORRENTE (M6, segurança): o current_limit clampa o SETPOINT, mas com zero FOC ruim a
        // corrente REAL sobe (foi o que deu 13A na bancada, ângulo errado). Se |Iq| medido passar do limiar
        // por > kOverIqMs, algo está errado (zero/ângulo) → desarma NA HORA. Deixa transiente curto passar.
        {
            static const float    kOverIqA  = 1.5f;   // A (acima do cap de 1A; pega sobrecorrente real)
            static const uint32_t kOverIqMs = 150u;   // ms tolerados acima do limiar
            static uint32_t s_overIqT = 0;
            const float iqAbs = fabsf(motor.current.q);
            if (iqAbs > kOverIqA) {
                if (s_overIqT == 0) s_overIqT = nowMs;
                if ((int32_t)(nowMs - s_overIqT) > (int32_t)kOverIqMs) {
                    engine.enableRequested = false;
                    g_fastPiActive = false; motor.disable();   // Stage 3b: para a ISR ANTES de desarmar (não re-energiza)
                    g_focPhase = FOC_IDLE; g_motorFault = true;
                    drivelab::dbgRingPrintf("FFB JOGO: CORTE por corrente Iq=%dmA (>%dmA por >%dms) — zero/angulo FOC ruim?\n",
                        (int)(iqAbs*1000.0f), (int)(kOverIqA*1000.0f), (int)kOverIqMs);
                    return;
                }
            } else s_overIqT = 0;
        }
        // ⚠️ Nota de unidade, NÃO-bloqueante: focMotor.setTorque(nm) faz motor.move(nm), e em foc_current o
        // move() trata o valor como CORRENTE (A). Então o "Nm" do engine é lido como "A" — a SEGURANÇA está
        // garantida pelo current_limit=1.0A (SimpleFOC clampa o Iq) independentemente disso; a calibração
        // Nm↔A é fidelidade, fica pra rampa futura.
        static uint32_t lastGfUs = 0;
        const uint32_t nowUs = micros();
        float dt = (nowUs - lastGfUs) * 1e-6f; lastGfUs = nowUs;
        if (dt <= 0.0f || dt > 0.05f) dt = 0.001f;   // sanidade do 1º passo / gaps
        engine.step(dt, focEncoder, focCurrent, focPower, focBrake, focMotor);  // força do jogo → torque (cap 1A)
        static uint32_t lastLogG = 0;
        if ((int32_t)(nowMs - lastLogG) > 400) { lastLogG = nowMs;
            drivelab::dbgRingPrintf("  FFB JOGO Iq=%dmA vel=%dmrad/s\n",
                (int)(motor.current.q*1000.0f), (int)(motor.shaft_velocity*1000.0f)); }
    } else if (g_ffbMode) {
        // MOLA + amortecimento: current_sp = -k*(ang-centro) - d*vel, capado em current_limit
        encoder.update();
        const float ang = encoder.getAngle() - g_ffbCenter;
        float cur = -g_ffbSpring * ang - g_ffbDamp * motor.shaft_velocity;
        const float lim = motor.current_limit;
        if (cur > lim) cur = lim; else if (cur < -lim) cur = -lim;
        motor.move(cur);                                             // torque mode → current_sp = cur
        static uint32_t lastLogF = 0;
        if ((int32_t)(nowMs - lastLogF) > 400) { lastLogF = nowMs;
            drivelab::dbgRingPrintf("  FFB ang=%ddeg cur_sp=%dmA Iq=%dmA\n",
                (int)(ang*57.2958f), (int)(cur*1000.0f), (int)(motor.current.q*1000.0f)); }
    } else if (g_sweepMode) {
        encoder.update();
        const float pos = encoder.getAngle();
        if (fabsf(pos - g_sweepTarget) < 0.3f)                       // chegou no alvo → inverte o lado
            g_sweepTarget = (g_sweepTarget > g_sweepCenter) ? (g_sweepCenter - kSweepAmp) : (g_sweepCenter + kSweepAmp);
        motor.move(g_sweepTarget);
        static uint32_t lastLogS = 0;
        if ((int32_t)(nowMs - lastLogS) > 400) { lastLogS = nowMs;
            drivelab::dbgRingPrintf("  SWEEP pos=%ddeg alvo=%ddeg vel=%dmrad/s Iq=%dmA\n",
                (int)((pos-g_sweepCenter)*57.2958f), (int)((g_sweepTarget-g_sweepCenter)*57.2958f),
                (int)(motor.shaft_velocity*1000.0f), (int)(motor.current.q*1000.0f)); }
    } else {
        float ff = 0.0f;                                            // feed-forward de cogging (se medido)
        if (g_hasCogging) { ff = g_coggingTable.compensation(encoder.getMechanicalAngle());
            if (ff > kCogFFClamp) ff = kCogFFClamp; else if (ff < -kCogFFClamp) ff = -kCogFFClamp; }
        motor.feed_forward_current.q = ff;   // feed-forward de CORRENTE (foc_current) — cancela o cogging
        motor.move(g_velTarget);
        static uint32_t lastLog = 0;
        if ((int32_t)(nowMs - lastLog) > 400) { lastLog = nowMs;
            drivelab::dbgRingPrintf("  RUN vel=%dmrad/s Iq=%dmA Id=%dmA Uq=%dmV ff=%dmV\n",
                (int)(motor.shaft_velocity*1000.0f), (int)(motor.current.q*1000.0f), (int)(motor.current.d*1000.0f),
                (int)(motor.voltage.q*1000.0f), (int)(ff*1000.0f)); }
    }
}

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
    (void)buffer;
    (void)bufsize;
    // Aloca o 1º bloco LIVRE (fix 400Hz): reusa blocos liberados em vez de um contador
    // com wrap cego em 40. 0 = pool cheio → o Block Load reporta status 2 (Full).
    g_lastEffectBlock = engine.effects.allocateBlock();
    // (sem printf: callback USB — não bloquear/estarvar o tud_task no handshake de entrada de pista)
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
        g_ffbMon[10] = report_id;   // instrumentação: último SET FEATURE
        if (report_id == RID_PID_CREATE_NEW_EFFECT)
        {
            handle_create_new_effect(buffer, bufsize);
        }
        return;
    }

    // Canal A0 primeiro — mesma ordem/motivo do m05.
    if (g_a0.handleOutReport(buffer, bufsize))
    {
        g_ffbMon[11]++;   // instrumentação: OUT report consumido pelo A0
        return;
    }

    // Chegou aqui = é um OUT report de FFB (não-A0) → o jogo está mandando FFB.
    // Reseta o watchdog de perda de sinal (SP3): se pararem de chegar reports
    // por >500ms, engine.step() decai a força a zero. Sem isto, o watchdog
    // zeraria a força mesmo com FFB ativo assim que o Stage 1 ligar o motor.
    engine.notifyFfbActivity();
    g_ffbMon[0]++;   // contador de reports FFB do jogo (openocd le → prova que o jogo manda FFB)
    g_pidStateDirty = true;   // pedir envio do PID State (confirma estado ao ACC — evita freeze)

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

    // Device Gain global (HID PID 0x0D): o host/OS manda 0-255 e escala a
    // força TOTAL em step() (via applyDeviceGain). Separado do EffectManager
    // — não é um efeito, é um ganho de dispositivo.
    if (bufsize >= 2 && buffer[0] == RID_PID_DEVICE_GAIN)
    {
        engine.setDeviceGain(buffer[1]);
        g_ffbMon[2] = buffer[1];
    }

    FfbOut o = ffb_parse_out(buffer, bufsize);
    switch (o.type)
    {
        case FFB_SET_CONSTANT_FORCE:
            // (sem printf: roda a CADA força constante — milhares/seg — e SerialTinyUSB.write()
            //  fica em loop enquanto o host tiver a porta CDC aberta; num callback USB isso estarva
            //  o tud_task() e contribui p/ o congelamento do jogo. Ver o throttle da telemetria acima.)
            // Só guarda o alvo (ForceReconstructor) — pura, sem hardware.
            // engine.step() (o que de fato leria isto e chamaria
            // motor.setTorque()) só roda atrás do gate `g_calibrated`
            // (sempre false aqui) — ver loop().
            engine.setGameForce(static_cast<float>(o.constantForce));
            g_ffbMon[1] = o.constantForce; g_ffbMon[5]++;
            break;

        case FFB_EFFECT_OPERATION:
            g_ffbMon[3] = o.op;
            break;

        case FFB_DEVICE_CONTROL:
            break;

        case FFB_SET_EFFECT:
        case FFB_BLOCK_LOAD:
        case FFB_UNKNOWN:
        default:
            break;
    }
}

// GET_REPORT Feature (Block Load / Pool) — mesmo handshake do m05.
static uint16_t hid_get_report_impl(uint8_t report_id,
                                    hid_report_type_t report_type,
                                    uint8_t *buffer, uint16_t reqlen)
{
    // PID State (0x02) é um report de INPUT (itens 0x81 no descritor), não
    // FEATURE — tratamos antes do guard de FEATURE. Só respondemos a
    // GET_REPORT; NÃO enviamos periodicamente (evita tráfego extra no IN
    // endpoint dividido com FFB/A0).
    if (report_type == HID_REPORT_TYPE_INPUT && report_id == RID_PID_STATE)
    {
        if (reqlen < 1)
        {
            return 0;
        }
        // 1 byte de status (bitfield, ver lib/base_usb/pid_state.h + descritor
        // 0x85,0x02). M5 sem motor: não pausado, atuadores habilitados, safety
        // ok, sem override, power presente.
        buffer[0] = buildPidStateByte(false /*devicePaused*/,
                                      true /*actuatorsEnabled*/,
                                      true /*safetySwitch*/,
                                      false /*actuatorOverride*/,
                                      true /*actuatorPower*/);
        return 1;
    }

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
        buffer[1] = (g_lastEffectBlock != 0) ? 1 : 2; // 1=Success; 2=Full (pool cheio → 0)
        // RAM pool REAL disponível (fix 400Hz): (slots LIVRES) * 16, refletindo a
        // alocação por reserva (allocateBlock/freeBlock). Antes descontava um contador
        // que dava wrap → o pool "crescia" do nada e confundia o ACC.
        uint16_t ramPoolAvailable =
            (uint16_t)((kMaxEffectBlocks - engine.effects.usedBlocks()) * 16);
        buffer[2] = (uint8_t)(ramPoolAvailable & 0xFF);
        buffer[3] = (uint8_t)(ramPoolAvailable >> 8);
        // (sem printf: este GET roda no handshake de criação de efeito na entrada de pista;
        //  bloquear/estarvar aqui era candidato ao congelamento — mantém a resposta imediata)
        return 4;
    }

    if (report_id == RID_PID_POOL)
    {
        if (reqlen < 4)
        {
            return 0;
        }
        // Tamanho do RAM pool: valor finito e plausível (não 0xFFFF genérico).
        // Budget de 16 bytes por slot de efeito -> kMaxEffectBlocks*16 = 640.
        uint16_t ramPoolSize = (uint16_t)(kMaxEffectBlocks * 16);
        buffer[0] = (uint8_t)(ramPoolSize & 0xFF);
        buffer[1] = (uint8_t)(ramPoolSize >> 8);
        buffer[2] = kSimultaneousEffectsMax; // efeitos simultâneos (Device Managed Pool)
        buffer[3] = 0;
        // (sem printf: callback USB no caminho de enumeração/FFB — resposta imediata)
        return 4;
    }

    return 0;
}

// Wrapper de instrumentação (2026-07-28): registra cada GET_REPORT (o que o ACC
// pede e o que respondemos) em g_ffbMon p/ diagnosticar o congelamento na entrada
// de pista via leitura SWD, sem printf (não-bloqueante).
static uint16_t hid_get_report_callback(uint8_t report_id,
                                        hid_report_type_t report_type,
                                        uint8_t *buffer, uint16_t reqlen)
{
    const uint16_t ret = hid_get_report_impl(report_id, report_type, buffer, reqlen);
    g_ffbMon[6]++;
    g_ffbMon[7] = ((int32_t)report_type << 8) | report_id;
    g_ffbMon[8] = ret;
    if (ret == 0) g_ffbMon[9]++;
    return ret;
}

// Ver o comentário completo em src/m05/main.cpp (has_out_endpoint=true, o
// descritor combinado FFB+A0 numa interface HID só) — idêntico aqui, mesma
// pilha (lib/base_usb/usb_base.{h,cpp}).
static Adafruit_USBD_HID *g_hid = nullptr;

// Janela de bus da BANCADA (M6): aplicada DEPOIS de todo applyCfgToEngine(), que deriva busMinV/busMaxV/
// overVoltageV a partir de busNominalV (ex.: nominal=56V vira 39-60V) e senão rejeitaria os ~19V da fonte
// de bring-up. Precisa ser chamada nos DOIS lugares que chamam applyCfgToEngine() (setup() e o cfgDirty()
// de loop()), senão um "Salvar" no app reaplica a janela derivada e o game-FFB para de aceitar a bancada.
// AJUSTAR/remover na rampa, quando a bancada for pra fonte nominal de verdade.
static void applyBenchBusWindow(drivelab::FfbEngine &e)
{
    e.startup.cfg.busMinV = 12; e.startup.cfg.busMaxV = 30;  // aceita 19V (bancada) — AJUSTAR na rampa
    e.guard.overVoltageV  = 30;
    // Teto de torque do engine (M6 feel): durável por cima do applyCfgToEngine (que deriva de maxTorqueLimit%).
    // 3.0 fica logo ACIMA do cap de corrente (2.5A), então quem realmente limita é o current_limit=2.5A
    // (clamp de HW). Sem isto, um maxTorqueLimit% baixo clamparia o torque abaixo do cap e a força sumiria.
    e.force.torqueLimitNm = 1.0f;   // teto gentil (casa com current_limit 1A do modo jogo); reavaliar com engine ligado
}

// ===== FOC FAST LOOP (Stage 1 — esqueleto a 8kHz; jeito ODrive/SimpleFOC real_time_loop) =====
// ISR dedicada num HardwareTimer (TIM6, básico, sem pino) a 8kHz — vai rodar o caminho rápido do FOC
// (genCurrentRead DIR-gated → Park → PI → setPhaseVoltage) FORA do loop() lento (~1,8kHz). STAGE 1 = só o
// esqueleto (conta ticks), SEM tocar no motor, pra verificar a taxa (por SWD) e que a USB não quebra.
// Prioridade NVIC ALTA (preempta a USB, cuja ISR é curtíssima e adia p/ o .task() no loop() — ver pesquisa).
static HardwareTimer* g_focTimer = nullptr;
static volatile uint32_t g_focIsrCount = 0;   // DIAG: conta ticks da ISR (confirmar 8kHz por SWD)
static void focFastLoopISR()
{
    g_focIsrCount++;
    // STAGE 3b (opção B): current loop RÁPIDO a 8kHz — SÓ a matemática de corrente (foc_current), replicada do
    // loopFOC do SimpleFOC (FOCMotor.cpp:640-657), usando o electrical_angle CACHEADO pelo loop lento
    // (slowFocSensorUpdate). O sensor NÃO é tocado aqui → evita a DUPLA atualização (a ISR e o loop lento
    // atualizarem o encoder = velocidade corrompida). Só nas fases FECHADAS (g_fastPiActive) + motor armado.
    // É o fix do overshoot do PI que estourava no loop lento (0,5A→6,5A): agora o PI roda a 8kHz (dt=125µs).
    if (g_fastPiActive && motor.enabled) {
        // FAIL-SAFE RUNAWAY na ISR (NÃO faminha como o corte do loop): lê o pulse_counter do encoder (atualizado
        // por interrupt, independe do loop). A cada ~5ms mede o delta; se implica velocidade alta, ZERA o PWM e
        // para o PI NA HORA (motor coasta) — mesmo com o loop faminto. Foi o gap que fez o usuário puxar o cabo 3x.
        static float s_isrAngRef = 0.0f; static uint16_t s_isrCntTick = 0;
        if (++s_isrCntTick >= 40) {   // ~5ms @ 8kHz
            s_isrCntTick = 0;
            const float a = encoder.getAngle();                 // ângulo cru do encoder (lê o count fresco)
            const float d = a - s_isrAngRef; s_isrAngRef = a;
            if (d > kIsrRunawayRad || d < -kIsrRunawayRad) {
                motor.setPhaseVoltage(0.0f, 0.0f, 0.0f);   // de-energiza (Uq=Ud=0 → sem torque)
                g_fastPiActive = false; g_isrRunaway = true; // o loop desarma de vez ao ver g_isrRunaway
                return;
            }
        }
        const float sp = _constrain(motor.current_sp, -motor.current_limit, motor.current_limit) + motor.feed_forward_current.q;
        // COMPENSA O ATRASO DO ÂNGULO (Option B: electrical_angle vem do loop lento ~1,8kHz → girando atrasa até
        // ~0,5ms → Park erra → Iq errático/oposto → torque ruim + oscilação). Prevê com a velocidade elétrica.
        const float angComp = motor.electrical_angle + motor.shaft_velocity * (float)motor.pole_pairs * kAngLagS;
        DQCurrent_s c = currentSense.getFOCCurrents(angComp);   // amostra coerente (adc_val) + Clarke/Park no ângulo previsto
        // ESCREVE motor.current (NÃO usar local!): a telemetria E o corte de sobrecorrente do FFB do jogo dependem disto.
        motor.current.q = motor.LPF_current_q(c.q);
        motor.current.d = motor.LPF_current_d(c.d);
        // FAIL-SAFE por SOBRECORRENTE (na ISR, não faminha): corrente presa alta por >5ms → de-energiza. O corte
        // de velocidade não pega corrente alta com vel~0 (o 13A do M6 com zero ruim). Reusa g_isrRunaway (loop desarma).
        static uint16_t s_ovIqTicks = 0;
        if (fabsf(motor.current.q) > kIsrOverIqA) {
            if (++s_ovIqTicks >= 40) {   // ~5ms sustentado
                motor.setPhaseVoltage(0.0f, 0.0f, 0.0f); g_fastPiActive = false; g_isrRunaway = true; return;
            }
        } else s_ovIqTicks = 0;
        // PI de corrente MANUAL com dt FIXO (o PID do SimpleFOC usa _micros() → congela o integrador na ISR de
        // 8kHz → Iq travava em 175mA com setpoint 813mA = mola fraca). Com dt fixo o integrador fecha a malha.
        const float dt = 1.0f / 8000.0f;
        const float eq = sp - motor.current.q;
        g_integQ = _constrain(g_integQ + g_curI * eq * dt, -motor.voltage_limit, motor.voltage_limit);
        const float Uq = _constrain(g_curP * eq + g_integQ, -motor.voltage_limit, motor.voltage_limit) + motor.feed_forward_voltage.q;
        const float ed = motor.feed_forward_current.d - motor.current.d;
        g_integD = _constrain(g_integD + g_curI * ed * dt, -motor.voltage_limit, motor.voltage_limit);
        const float Ud = _constrain(g_curP * ed + g_integD, -motor.voltage_limit, motor.voltage_limit) + motor.feed_forward_voltage.d;
        motor.setPhaseVoltage(Uq, Ud, angComp);   // aplica no ângulo previsto (compensado)
    }
}
static void focFastLoopBegin()
{
    g_focTimer = new HardwareTimer(TIM6);
    g_focTimer->setOverflow(8000u, HERTZ_FORMAT);   // 8kHz (mesma razão do ODrive vs o PWM de 24kHz)
    g_focTimer->setInterruptPriority(1, 0);         // prioridade alta (ODrive: current loop prio 0; USB fica bem abaixo)
    g_focTimer->attachInterrupt(focFastLoopISR);
    g_focTimer->resume();
    drivelab::dbgRingPrintf("FOC FAST LOOP: TIM6 @ 8kHz armado (Stage 1 stub, prio 1)\n");
}

void setup()
{
    // EnterDfu — PRIMEIRÍSSIMA coisa de setup(), antes de qualquer init de
    // USB/clock. Ver lib/base_usb/dfu_jump.{h,cpp}.
    dfuCheckAtBootOrJump();

    // Mantém fw_signature.h no binário final (--gc-sections) — mesmo truque
    // de inline asm documentado em src/m05/main.cpp (os dois caminhos mais
    // fracos que FALHARAM estão comentados lá; não repetido aqui).
    __asm__ __volatile__("" : : "r"(&fw_signature) : "memory");

    // Causa do ÚLTIMO reset (RCC_CSR) — diagnóstico do reset sob o chopper (bancada 2026-07-31).
    // " BOR" = brown-out (dip de VDD/bus), " IWDG"/" WWDG" = watchdog, " PIN"/" POR" = power/NRST glitch,
    // " SFT" = software. Ler ANTES de limpar; fica no dbgRing p/ ler por SWD/CDC quando a USB subir.
    {
        const uint32_t csr = RCC->CSR;
        drivelab::dbgRingPrintf("RESET cause: CSR=0x%08lx%s%s%s%s%s%s\n", (unsigned long)csr,
            (csr & RCC_CSR_BORRSTF)  ? " BOR"  : "",
            (csr & RCC_CSR_PINRSTF)  ? " PIN"  : "",
            (csr & RCC_CSR_PORRSTF)  ? " POR"  : "",
            (csr & RCC_CSR_SFTRSTF)  ? " SFT"  : "",
            (csr & RCC_CSR_IWDGRSTF) ? " IWDG" : "",
            (csr & RCC_CSR_WWDGRSTF) ? " WWDG" : "");
        RCC->CSR |= RCC_CSR_RMVF;   // limpa os flags → próxima leitura reflete só o próximo reset
    }

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
    // Fault de COLD-BOOT do DRV8301: no POR o configure() roda antes do charge-pump/GVDD estabilizar → faulted()
    // (o SPI já responde, mas o fault flag está setado por undervoltage). Warm reset "recuperava" só porque o
    // charge-pump já estava de pé. Fix = retry com settle crescente; cada configure() re-cicla EN_GATE (LOW→HIGH)
    // → limpa o fault latchado + dá tempo pra subir. ~500ms de pior caso, só no boot.
    // COLD-BOOT: o charge-pump/GVDD do DRV precisa de MUITO mais settle que o warm (20ms) — sem isto o configure
    // falha com GVDD_UV e exige a dança de warm reset (bancada 2026-08-02). O settle agora é DENTRO do configure
    // (após EN_GATE alto), crescente 300ms→~1s, até 6x. Rápido se configurar de 1ª; ~4,6s no pior caso.
    bool drvOk = drv.configure(300);
    for (int attempt = 1; !drvOk && attempt <= 6; ++attempt) {
        const uint32_t settle = 300u + 120u * (uint32_t)attempt;   // 420, 540, ... 1020ms
        drvOk = drv.configure(settle);
        drivelab::dbgRingPrintf("DRV8301 cold-boot retry %d/6 (settle %lums): %s\n", attempt, (unsigned long)settle, drvOk ? "OK" : "ainda FAIL");
    }
    if (!drvOk) {
        uint16_t s1 = 0, s2 = 0; const bool spiok = drv.readStatusRegs(s1, s2);
        drivelab::dbgRingPrintf("DRV DIAG: nFAULT=%d SPIok=%d SR1=0x%03x SR2=0x%03x (SR1: b0-5=OC das FETs, b6=OTW, b7=OTSD, b8=PVDD_UV, b9=GVDD_UV, b10=FAULT)\n",
                             drv.nFaultActive() ? 1 : 0, spiok ? 1 : 0, (unsigned)s1, (unsigned)s2);
    }

    // ---- Motor em modo torque por tensão — só CAMPOS, sem init/enable ----
    motor.torque_controller = TorqueControlType::voltage;
    motor.controller        = MotionControlType::torque;
    // Limite de tensão BEM conservador — só importa quando (Stage 1) motor
    // estiver de fato ligado; aqui é só o valor que já fica guardado no
    // objeto para esse dia.
    motor.voltage_limit = 2.0f;

    // ---- Config do "cérebro" (lib/brain) — CONSERVADORA ----
    engine.force.maxTorqueNm   = 2.5f;  // escala nominal (revertido: a escala 15 foi chute sobre dado ruim — o engine estava DESLIGADO). Reavaliar com o engine ligado
    engine.force.torqueLimitNm = 1.0f;  // teto DURO de segurança, baixo neste passo
    engine.currentLimitA       = 1.5f;  // corte por sobrecorrente bem conservador
    engine.enableRequested     = false; // NUNCA setado true neste arquivo
    // A janela de tensão (busMin/busMax/overVoltage) NÃO é mais hardcode: vem da tensão nominal escolhida pelo
    // usuário (BID_BUS_NOMINAL_V), aplicada por applyCfgToEngine() abaixo — o DIYer casa com a fonte dele.

    // ---- USB (HID FFB+A0 combinado + CDC) — lib/base_usb/usb_base.{h,cpp} ----
    UsbBase::setReportCallbacks(hid_get_report_callback, hid_set_report_callback);
    g_hid = &UsbBase::begin();

    // Encoder: LEITURA habilitada (contagem A/B por interrupção). Isto NÃO liga o motor — não toca no
    // gate/PWM do DRV8301, só configura os pinos do encoder e as ISRs. Permite ler posição/velocidade e o
    // set-center com o motor OFF (validação de bancada sem 56V). init() é não-bloqueante (ao contrário do
    // initFOC(), que faz varredura e continua fora de escopo aqui).
    analogReadResolution(12);   // ADC de 12 bits (0..4095) — as conversões (bus, NTC) assumem 4096 de fundo

    encoder.init();
    encoder.enableInterrupts(doEncoderA, doEncoderB);

    // Brake chopper (TIM2): configura no estado DESARMADO/seguro. NO-OP no build padrão (só age se
    // DRVLAB_BRAKE_CHOPPER_HW estiver definido) — e mesmo então não dissipa até arm() (passo de bancada).
    focBrake.begin();

#ifdef DRVLAB_OLED
    // Visor OLED (opcional): botão com pull-up interno + init do display. begin() detecta presença por
    // ACK — se o OLED não estiver ligado, vira NO-OP e o firmware segue normal (não trava).
    pinMode(kOledPinButton, INPUT_PULLUP);
    g_oled.begin();
#endif

    // Canal A0: carrega os settings persistidos (ou semeia defaults).
    g_a0.begin();

    // CPR do encoder VEM DO CONFIG (encoderCpr) — trocar encoder no futuro = só mudar no app + Salvar, sem reflash.
    // O construtor do Encoder e o encoder.init() já rodaram com o DEFAULT; cpr só é usado em runtime, então
    // sobrescrever aqui (depois de ler a flash) já vale para o 1º positionRad() do loop. 0/absurdo → fallback.
    g_encCpr = static_cast<float>(g_a0.cfg().encoderCpr);
    if (g_encCpr < 1.0f) g_encCpr = ENC_CPR;
    encoder.cpr = g_encCpr;

    // encoderType (BID 18): v1 só suporta ABI (E6B2). Tipo magnético (MT6701/AS5047P) é reconhecido
    // mas fica pro M5 (SPI3 compartilhado com o DRV8301 + CS) → cai de volta pro ABI com aviso.
    {
        const auto encSel = selectEncoder(g_a0.cfg().encoderType);
        if (!encSel.supported)
            drivelab::dbgRingPrintf("encoderType=%u nao suportado (v1); usando ABI/E6B2\n", g_a0.cfg().encoderType);
    }

    // Sub-projeto 1 (Feel ajustável ao vivo, Task 3): aplica os settings
    // carregados por cima do que acabou de ser hardcoded acima. Ordem
    // importa -- maxTorqueNm é constante de HARDWARE (fica setado antes),
    // torqueLimitNm/demais campos de "feel" são SETTINGS e vêm de
    // applyCfgToEngine() (sobrescreve o valor conservador fixo logo acima
    // por um derivado de c.maxTorqueLimit%). busMinV/busMaxV/overVoltageV
    // (guard/startup, safety-critical) SÃO derivados de busNominalV por
    // applyCfgToEngine (ver lib/base_shared/apply_cfg.h) -- por isso a
    // janela de bancada é reaplicada por cima logo abaixo, e de novo em
    // todo cfgDirty() de loop() (senão um "Salvar" no app restaura a
    // janela derivada de busNominalV e rejeita a fonte de bring-up).
    applyCfgToEngine(g_a0.cfg(), engine, kLoopHz);
    applyBenchBusWindow(engine);  // M6, Task 2 -- ver comentário no helper acima

    // Contator fail-safe (Task 4, opt-in por soft_power_enable): deixa o pino em estado seguro
    // (bobina OFF -> contator ABERTO) no boot. Com softPowerEnable=0 (default) nada aqui executa —
    // nenhum GPIO é tocado, o comportamento é idêntico ao de hoje.
    if (g_a0.cfg().softPowerEnable) {
        pinMode(kOdrivePinContactor, OUTPUT);
        digitalWrite(kOdrivePinContactor, LOW);   // bobina OFF -> contator ABERTO (fail-safe no boot)
        g_contactorPinInit = true;
        drivelab::dbgRingPrintf("SOFT-POWER: contator ativo (pino %d), aberto no boot\n", kOdrivePinContactor);
    }

    // Soft-power por botão (Task 4, opt-in por power_button_enable): latch de energia (self-hold) HIGH
    // no boot + botão ativo-baixo com pull-up. Com powerButtonEnable=0 (default) nada aqui executa —
    // nenhum GPIO é tocado, o comportamento é idêntico ao de hoje.
    if (g_a0.cfg().powerButtonEnable) {
        pinMode(kOdrivePinPowerEnable, OUTPUT);
        digitalWrite(kOdrivePinPowerEnable, HIGH);   // latch: segura a própria energia no boot
        pinMode(kOdrivePinPowerBtn, INPUT_PULLUP);   // botão ativo-baixo (ajustar ao hardware)
        drivelab::dbgRingPrintf("SOFT-POWER: botão de power ativo (btn=pino %d, enable=pino %d)\n",
                                 kOdrivePinPowerBtn, kOdrivePinPowerEnable);
    }

    // Divisor do VBUS pela VARIANTE DA PLACA (24V→11, 56V→19) — propriedade do hardware, independente da
    // tensão de operação. Um binário só lê certo nas duas placas, sem recompilar. (Não há auto-detecção por
    // hardware — nem o firmware oficial do ODrive detecta; usa #define de compile-time.)
    focPower.setDividerRatio(vbusDividerForVariant(g_a0.cfg().boardVariant));

    // Tabela de cogging (por-motor): lê o blob da flash e, se for válido (magic/versão/N/CRC batem),
    // liga no engine — respeitando o setting coggingEnable. Se não houver tabela (placa nova/sem calibrar),
    // segue sem compensação. A calibração que ESCREVE o blob é da bancada (Stage 1, exige motor).
    {
        uint8_t blob[drivelab::coggingBlobSize<128>()];
        EEPROM.get(kCoggingFlashAddr, blob);
        g_hasCogging = drivelab::unpackCogging(blob, sizeof(blob), g_coggingTable);
        applyCoggingToEngine();
        drivelab::dbgRingPrintf("DriveLab M5 — cogging: %s\n",
                             g_hasCogging ? "tabela carregada da flash" : "sem tabela (sem compensacao)");
    }

    drivelab::dbgRingPrintf("DriveLab M5 (Task 4) — DRV8301 configure()=%s ready=%s faulted=%s | motor OFF (sem init/enable/initFOC)\n",
                  drvOk ? "OK" : "FAIL",
                  drv.isReady() ? "true" : "false",
                  drv.faulted() ? "true" : "false");

    focFastLoopBegin();   // Stage 1: ISR do FOC rápido a 8kHz (stub) — depois da USB estar de pé
}

void loop()
{
    // EnterDfu — mesma regra do m05 (fora do contexto do callback USB).
    if (g_a0.dfuRequested())
    {
        dfuRequestJump();
    }

    TinyUSBDevice.task();
    g_ffbMon[15]++;   // contador de iterações do loop (SWD): mede a taxa real do loop @400Hz

    if (!TinyUSBDevice.mounted())
    {
        delay(2);
        return;
    }

    static bool bannerSent = false;
    if (!bannerSent)
    {
        bannerSent = true;
        drivelab::dbgRingPrintf("DriveLab M5 (Task 4) — USB/A0 + FFB->engine ativos | motor OFF (g_calibrated=false)\n");
    }

    // Canal A0: SaveSettings — escrita de fato na flash fora do callback USB.
    if (g_a0.saveRequested())
    {
        g_a0.clearSave();
        g_a0.save();
        drivelab::dbgRingPrintf("A0 saved\n");
    }

    uint32_t now = millis();

    // Contator fail-safe (Task 4, opt-in por soft_power_enable): passo da máquina de estados + aciona
    // a bobina. Com softPowerEnable=0 (default) nada aqui executa — nenhum GPIO é tocado.
    if (g_a0.cfg().softPowerEnable) {
        const bool driveIntent = (g_focPhase != FOC_IDLE) || g_driveWanted;
        g_contactor.step(driveIntent, g_motorFault, now);
        if (g_contactorPinInit) digitalWrite(kOdrivePinContactor, g_contactor.coilOn() ? HIGH : LOW);
    }

    // Soft-power por botão (Task 4, opt-in por power_button_enable): passo da máquina de estados +
    // aciona o latch de energia. Com powerButtonEnable=0 (default) nada aqui executa — nenhum GPIO é
    // tocado, comportamento idêntico ao de hoje.
    if (g_a0.cfg().powerButtonEnable) {
        // botão ativo-baixo com pull-up → pressionado = LOW. Debounce (reusa o mesmo debouncer do
        // botão do OLED) — só o estado ESTÁVEL (pressed()) importa aqui, não a borda de clique, porque
        // o PowerButton precisa medir quanto tempo o botão fica segurado.
        g_powerBtnDebounce.update(digitalRead(kOdrivePinPowerBtn) == LOW, now);
        bool btnDown = g_powerBtnDebounce.pressed();
        // se o contator está desabilitado, não há o que esperar abrir → trata como "aberto"
        bool contactorOpen = !g_a0.cfg().softPowerEnable
                             || (g_contactor.state() == ContactorState::Open);
        g_powerButton.step(btnDown, contactorOpen, now);

        if (g_powerButton.shuttingDown()) {
            // desarma o motor e pede o contator abrir (driveIntent=false via g_driveWanted)
            g_driveWanted = false;
            g_focPhase = FOC_IDLE;
            if (motor.driver != nullptr) motor.disable();   // driver só é linkado após um Calibrate; sem isso, disable() null-derefa
        }
        digitalWrite(kOdrivePinPowerEnable, g_powerButton.powerEnable() ? HIGH : LOW);
    }

    static uint32_t lastSensor = 0;
    if (!g_csReady && now - lastSensor >= 100)   // ⚠️ analogRead do MCU temp DE-INICIALIZA o ADC → quebra o injected
    {
        lastSensor = now;
        sensorsSample();
    }

    // Força aditiva de efeitos por telemetria (app → report DIRECT 0x10). Aplica quando chega um valor novo e
    // zera por timeout se o app parar de enviar (jogo fechado / efeitos desligados) — não deixa força presa.
    {
        static uint32_t lastDirectMs = 0;
        int16_t tf;
        if (g_a0.consumeTelemetryForce(tf))
        {
            engine.setTelemetryForce((float)tf);
            lastDirectMs = now;
        }
        else if (lastDirectMs != 0 && now - lastDirectMs > 250)
        {
            engine.setTelemetryForce(0.0f);
            lastDirectMs = 0;
        }
    }

    // Encoder + set-center (motor OFF): atualiza a contagem, atende o pedido de "centralizar"
    // (BaseCommand.ResetCenter) e publica posição/ângulo RELATIVOS ao centro na telemetria (o "0°" do app).
    // Puro leitura — não liga o motor.
    {
        encoder.update();
        if (g_a0.centerRequested())
            focEncoder.setCenterHere();

        // STAGE 1a — calibração FOC + giro (bancada, operador de mão na tomada). NÃO-bloqueante: stage1aStart()
        // configura+habilita; o resto roda no stage1aTick() (todo loop) e para sozinho — a telemetria NUNCA para.
        //   cmd 5 (Calibrate)       → cal de zero + RUN (aplica cogging SE já medido).
        //   cmd 7 (CalibrateCogging)→ cal de zero + MEDE o cogging (giro lento) + RUN COM compensação.
        // Só inicia se estiver ocioso (não interrompe um teste em andamento).
        uint8_t calArg = 0;
        const bool wantCal = g_a0.calibrateRequested(calArg);
        const bool wantCog = g_a0.coggingCalibRequested();
#ifdef DRVLAB_BRAKE_CHOPPER_HW
        // Bancada do brake chopper (cmd A0=8): arma/aplica duty manual no FocBrake, fora do callback USB.
        // Sempre desabilita o motor e força g_focPhase=FOC_IDLE — o duty manual (abaixo) é a AUTORIDADE
        // sobre o chopper enquanto g_brakeBenchActive; nenhum stage1a/engine.step roda nesta janela (guardado
        // logo abaixo, "!g_brakeBenchActive", contra um Calibrate concorrente reabrir o FOC por engano).
        {
            uint8_t brakeArg;
            if (g_a0.brakeBenchRequested(brakeArg)) {
                BrakeBenchState b = brakeBenchCommand(brakeArg);
                if (motor.driver != nullptr) motor.disable();   // driver só é linkado após um Calibrate; sem isso, disable() null-derefa
                g_focPhase = FOC_IDLE;
                if (b.exitBench) {
                    focBrake.disarm();
                    g_brakeBenchActive = false;
                    drivelab::dbgRingPrintf("BRAKE: desarmado\n");
                } else if (focPower.busVoltage() < 4.0f) {
                    // Gate estilo OpenFFBoard: NÃO armar/dumpar com o bus baixo (evita loop de reset —
                    // eles gatearam o brake quando só-USB pelo mesmo motivo). Mesmo limiar do stage1aStart.
                    focBrake.disarm();
                    g_brakeBenchActive = false;
                    drivelab::dbgRingPrintf("BRAKE: recusado — bus baixo %dmV (min 4000mV)\n",
                        (int)(focPower.busVoltage() * 1000.0f));
                } else {
                    if (!focBrake.armed()) focBrake.arm();
                    g_brakeDutyManual  = b.dutyPct;
                    g_brakeBenchActive = true;
                    g_brakeAutoActive  = false;   // mútua exclusão: bancada manual desarma o modo automático
                    g_brakeBenchT      = now;
                    drivelab::dbgRingPrintf("BRAKE: armed duty=%d%%\n", (int)b.dutyPct);
                }
            }
        }
        if (g_brakeBenchActive) {
            if (g_motorFault || (int32_t)(now - g_brakeBenchT) > (int32_t)kBrakeBenchTimeoutMs) {
                focBrake.disarm();
                g_brakeBenchActive = false;
                drivelab::dbgRingPrintf("BRAKE: auto-desarme (timeout/fault)\n");
            } else {
                focBrake.setDuty(g_brakeDutyManual / 100.0f);   // duty manual (setDuty já valida dead-time)
            }
        }
        // Modo auto-brake de bancada (cmd A0=9): arma o FocBrake e deixa o BrakeController (ffb_power.h)
        // dirigir o duty pela tensão do bus, a partir de limiares auto-calibrados no ocioso (brake_auto.h).
        // Mesmo padrão de segurança do cmd=8: motor SEMPRE off, gate de bus baixo, auto-desarme por timeout/fault.
        {
            uint8_t autoArg;
            if (g_a0.brakeAutoRequested(autoArg)) {
                if (motor.driver != nullptr) motor.disable();   // motor SEMPRE off no teste do freio
                g_focPhase = FOC_IDLE;
                g_brakeBenchActive = false;                      // mútua exclusão com o cmd=8 manual
                if (autoArg == 0) {
                    focBrake.disarm();
                    g_brakeAutoActive = false;
                    drivelab::dbgRingPrintf("BRAKE AUTO: desarmado\n");
                } else if (focPower.busVoltage() < 4.0f) {
                    focBrake.disarm();
                    g_brakeAutoActive = false;
                    drivelab::dbgRingPrintf("BRAKE AUTO: recusado — bus baixo %dmV\n",
                        (int)(focPower.busVoltage() * 1000.0f));
                } else {
                    const float idle = focPower.busVoltage();
                    const BrakeAutoThresholds th = brakeAutoThresholds(idle);
                    g_brakeAutoCtrl.cfg.onVoltage     = th.onVoltage;
                    g_brakeAutoCtrl.cfg.fullVoltage   = th.fullVoltage;
                    g_brakeAutoCtrl.cfg.offVoltage    = th.offVoltage;
                    g_brakeAutoCtrl.cfg.resistanceOhm = kBrakeAutoResOhm;
                    g_brakeAutoCtrl.cfg.maxCurrentA   = kBrakeAutoMaxCurrA;
                    if (!focBrake.armed()) focBrake.arm();
                    g_brakeAutoActive = true;
                    g_brakeAutoT      = now;
                    drivelab::dbgRingPrintf("BRAKE AUTO: armado on=%dmV full=%dmV off=%dmV\n",
                        (int)(th.onVoltage * 1000.0f), (int)(th.fullVoltage * 1000.0f), (int)(th.offVoltage * 1000.0f));
                }
            }
        }
        if (g_brakeAutoActive) {
            if (g_motorFault || (int32_t)(now - g_brakeAutoT) > (int32_t)kBrakeBenchTimeoutMs) {
                focBrake.disarm();
                g_brakeAutoActive = false;
                drivelab::dbgRingPrintf("BRAKE AUTO: auto-desarme (timeout/fault)\n");
            } else {
                const float busV = focPower.busVoltage();
                const float duty = g_brakeAutoCtrl.update(busV);
                focBrake.setDuty(duty);   // o controlador dirige o duty
                // Diagnóstico (bancada): loga a transição DESLIGADO->LIGADO — o chopper "disparou sozinho".
                // O dump pode ser breve/pequeno demais pra sentir no resistor, mas o disparo aparece aqui (SWD/CDC).
                static bool prevAutoOn = false;
                if (g_brakeAutoCtrl.isOn() && !prevAutoOn) {
                    drivelab::dbgRingPrintf("BRAKE AUTO: DISPAROU! busV=%dmV duty=%d%%\n",
                                            (int)(busV * 1000.0f), (int)(duty * 100.0f));
                }
                prevAutoOn = g_brakeAutoCtrl.isOn();
            }
        }
#endif
        if (g_focPhase == FOC_IDLE
#ifdef DRVLAB_BRAKE_CHOPPER_HW
            && !g_brakeBenchActive   // não deixa um Calibrate concorrente reabrir o FOC durante a bancada do freio
            && !g_brakeAutoActive    // idem para o modo auto-brake
#endif
        )
        {
            if (wantCog)                       stage1aStart(now, 4, true);   // mede cogging a 4 rad/s no RUN
            else if (wantCal && calArg >= 100) currentSenseDiag(now, calArg); // 100=init/repouso do current sense
            else if (wantCal)                  stage1aStart(now, calArg, false);
        }
        stage1aTick(now);
        const float angRad = focEncoder.positionRad();                       // já relativo ao centro
        const int16_t deci = A0Channel::angleDeciDegFromRad(angRad);
        const long counts  = lroundf(angRad * (g_encCpr / (2.0f * 3.14159265358979323846f)));
        const int16_t pos  = static_cast<int16_t>(counts > 32767 ? 32767 : (counts < -32768 ? -32768 : counts));
        g_a0.setWheelTelemetry(pos, deci);
        g_ffbMon[4] = deci;   // posição do volante (deci-deg) — o jogo lê isto p/ calcular o FFB

        // Tensão do barramento (ADC PA6) → telemetria. Leitura passiva (motor OFF ok). A escala do divisor
        // ainda é placeholder (item A1 do registro de validação) — este valor serve p/ calibrar vs multímetro.
        // ⚠️ analogRead QUEBRA o ADC injected do current sense (limitação conhecida do SimpleFOC STM32 —
        // analogRead init/deinit o ADC toda vez). Enquanto o current sense estiver ativo (g_csReady), NÃO ler
        // tensão do bus / temps por analogRead — congela esses valores (o encoder/posição não usa ADC, segue ok).
        //
        // ⚠️ PERF/USB (2026-07-28): cada analogRead é CARO e BLOQUEANTE — pinmap_find_function +
        // HAL_ADC_Start + HAL_ADC_PollForConversion. Lê-los TODO loop (4 analogReads: bus + 2 FET + motor)
        // dominava o tempo do loop (confirmado por profiling do PC via SWD: o firmware passava a maior parte
        // em analogRead) e ESTARVA o tud_task(): quando o ACC entrava na pista e despejava a enxurrada de FFB,
        // as transferências de controle do carregamento não eram atendidas a tempo e o JOGO CONGELAVA na tela
        // (tirar a USB → entrava na hora). Fix: throttle a 50 ms (20 Hz, de sobra p/ a tela do app), cacheando
        // busV p/ o flag de plausibilidade. Loop rápido → USB atendido. Ver [[drivelab-p0-hid-ep-fix]].
        static float s_lastBusV = 0.0f;
        static uint32_t s_lastTelemMs = 0;
        if (!g_csReady && (now - s_lastTelemMs >= 50)) {
            s_lastTelemMs = now;
            s_lastBusV = focPower.busVoltage();
            uint16_t busMv = 0;
            if (s_lastBusV > 0.0f) { const float mv = s_lastBusV * 1000.0f; busMv = mv > 65535.0f ? 65535u : static_cast<uint16_t>(mv + 0.5f); }
            g_a0.setBusVoltageMv(busMv);

            long fetC = lroundf(focPower.mosfetTempC());
            if (fetC > 127) fetC = 127; else if (fetC < -128) fetC = -128;
            g_a0.setFetTempC(static_cast<int8_t>(fetC));
            g_a0.setFetNtcRaw(focPower.mosfetNtcRaw());

            long motC = lroundf(focPower.motorTempC());
            if (motC > 127) motC = 127; else if (motC < -128) motC = -128;
            g_a0.setMotorTempC(static_cast<int8_t>(motC));
        }
        const float busV = g_csReady ? 0.0f : s_lastBusV;

        // Plausibilidade: com o bus energizado, avisa se a tensão lida não bate com a variante escolhida.
        const uint8_t flags = ((!g_csReady && busVoltageImplausible(busV, g_a0.cfg().busNominalV)) ? A0Channel::kFlagVoltageImplausible : 0)
                            | (g_hasCogging ? A0Channel::kFlagCoggingLoaded : 0)
                            | (g_motorReady ? 0x02 : 0)   // Calibrated (BaseFlags) — initFOC OK (Stage 1a)
                            | (g_motorFault ? 0x04 : 0);  // Error — initFOC falhou
        g_a0.setStatusFlags(flags);
    }

    // Medição de clipping do FFB mesmo com o motor OFF: mede a demanda do host (força constante da tela de
    // Teste / efeitos PID do jogo) contra o teto de torque e publica na telemetria (byte 19). Deskside, sem
    // 56V — o motor não gira; só medimos o quanto a força pedida passaria do teto. No Stage 1 (g_calibrated),
    // é o próprio engine.step() que atualiza o clip meter; aqui só o publicamos.
    {
        // PERF/400Hz (2026-07-29): o measureClipOnly chama gameDemandRaw → computeForce
        // (itera 40 slots) + computeTorqueRaw (endstop/filtros) — CARO. Rodá-lo TODO loop
        // (milhares/seg) dominava o loop (visto por SWD) e estarvava o USB → o ACC a 400Hz
        // não era atendido a tempo e TRAVAVA no carregamento da sessão. Com o motor OFF a
        // força medida é só telemetria (medidor de clipping do app), então THROTTLAMOS a
        // ~50Hz — libera o loop p/ aguentar 400Hz de FFB. (Quando o Stage 1 ligar o motor,
        // é o engine.step() que atualiza o clip, sem passar por aqui.) Ver
        // [[drivelab-game-compat-a0]] (fix dos 400Hz do ACC).
        static uint32_t lastClipUs = 0;
        static uint32_t lastClipMs = 0;
        if (now - lastClipMs >= 20)   // ~50Hz — de sobra p/ o medidor de clipping do app
        {
            lastClipMs = now;
            const uint32_t nowUs = micros();
            const float clipDt = lastClipUs ? (nowUs - lastClipUs) * 1e-6f : 0.02f;
            lastClipUs = nowUs;
            if (!(g_calibrated && g_a0.forceEnabled()))
                engine.measureClipOnly(clipDt);
            g_a0.setClipping(engine.clipping());
        }
    }

#ifdef DRVLAB_OLED
    // Visor OLED: o botão (PA2) cicla as telas TODO loop (não perde clique); o refresh é throttled (~4Hz)
    // e só roda quando a força NÃO está ativa — assim nunca rouba tempo do loop de torque no Stage 1
    // (agora, motor OFF, atualiza sempre). NO-OP se o OLED não respondeu no begin().
    {
        if (g_oledButton.update(digitalRead(kOledPinButton) == LOW, now))
            g_oledPage = drivelab::oledWrapPage(g_oledPage + 1);

        static uint32_t lastOledMs = 0;
        if (g_oled.present() && (now - lastOledMs) >= 250 && !(g_calibrated && g_a0.forceEnabled())) {
            lastOledMs = now;
            drivelab::OledStatus st;
            st.verMajor = DRVLAB_FW_VER_MAJOR;
            st.verMinor = DRVLAB_FW_VER_MINOR;
            st.verPatch = DRVLAB_FW_VER_PATCH;
            const float bv = focPower.busVoltage();
            st.busMilliV   = bv > 0.0f ? static_cast<int>(bv * 1000.0f) : 0;
            st.fetTempC    = static_cast<int>(lroundf(focPower.mosfetTempC()));
            st.mcuTempC    = sensorMcuTempC();
            st.clippingPct = static_cast<int>(engine.clipping()) * 100 / 255;
            st.angleDeciDeg = A0Channel::angleDeciDegFromRad(focEncoder.positionRad());
            st.forceActive = g_calibrated && g_a0.forceEnabled();
            st.voltageImplausible = busVoltageImplausible(bv, g_a0.cfg().busNominalV);
            drivelab::renderStatus(g_oledCanvas, st, g_oledPage);
            g_oled.flush(g_oledCanvas);
        }
    }
#endif

    // >>> PRIORIDADE ABSOLUTA DO REPORT DO VOLANTE (2026-07-28). O jogo LÊ este report
    // (RID_JOYSTICK) pro input de direção. O endpoint IN é COMPARTILHADO e, sob a carga
    // de FFB, libera vaga devagar. Se a telemetria A0 (0x21) e o PID State (0x02) rodarem
    // ANTES, roubam TODAS as vagas livres e o report do volante NUNCA sai → o jogo fica
    // "sem ação" (dispositivo enumerado, eixo morto; volta ao fechar o jogo). Confirmado
    // por SWD: [12] joystick CONGELADO, [13] endpoint-ocupado disparando, [14] PID/telem
    // ainda enviando. Fix: mandar o JOYSTICK PRIMEIRO; A0/PID pegam só a sobra do endpoint.
    static uint32_t lastSend = 0;
    if (now - lastSend >= 4)   // ~250Hz p/ FFB responsivo
    {
        if (g_hid->ready())
        {
            lastSend = now;
            JoystickInputReport report;
            memset(&report, 0, sizeof(report));
            // ±900° (±15.7 rad) → full-scale int16. Posição relativa ao centro (focEncoder).
            float a = focEncoder.positionRad() / 15.708f * 32767.0f;
            if (a > 32767.0f) a = 32767.0f; else if (a < -32767.0f) a = -32767.0f;
            report.axes[0] = (int16_t)a;
            UsbBase::sendReport(RID_JOYSTICK, (const uint8_t *)&report, sizeof(report));
            g_ffbMon[12]++;   // instrumentação: joystick enviado
        }
        else
        {
            g_ffbMon[13]++;   // instrumentação: queria mandar joystick mas endpoint IN OCUPADO
        }
    }

    // Resposta deferida do A0 (0x16) + telemetria periódica (0x21) — SÓ pega o endpoint IN
    // se o joystick acima não pegou (o sender interno checa g_hid->ready()). Assim a
    // telemetria do app nunca mais starva o input do jogo.
    g_a0.serviceLoop(now, &UsbBase::sendReport);

    // PERF/400Hz (2026-07-29): o TinyUSBDevice.task() (que DRENA o endpoint OUT de FFB)
    // roda só 1× no topo do loop → a taxa de serviço do USB = taxa do loop. A 400Hz o
    // ACC despeja FFB rápido demais e o OUT enche antes do próximo task() → NAK → o ACC
    // trava. Chamamos o task() DE NOVO no meio do loop (aqui e no fim) p/ drenar o OUT
    // com mais frequência, sem depender do loop inteiro. Ver [[drivelab-game-compat-a0]].
    TinyUSBDevice.task();

    // Sub-projeto 1 (Feel ajustável ao vivo, Task 3): ao vivo -- só reaplica
    // quando algum SETWRITE (0x14) de fato mexeu no BaseCfg (cfgDirty()),
    // nunca todo tick (applyCfgToEngine não é grátis: monta um Biquad
    // sempre que chamado).
    if (g_a0.cfgDirty())
    {
        applyCfgToEngine(g_a0.cfg(), engine, kLoopHz);
        applyBenchBusWindow(engine);  // M6: reaplica a janela de bancada (applyCfgToEngine acabou de sobrescrevê-la)
        focPower.setDividerRatio(vbusDividerForVariant(g_a0.cfg().boardVariant)); // re-deriva ao trocar a variante
        applyCoggingToEngine(); // honra coggingEnable ao vivo (liga/desliga a tabela sem reboot)
        g_encCpr = static_cast<float>(g_a0.cfg().encoderCpr);  // CPR ao vivo (muda no app + Salvar, sem reflash)
        if (g_encCpr < 1.0f) g_encCpr = ENC_CPR;
        encoder.cpr = g_encCpr;
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
    // O caminho jogo→motor da v1 é o modo de comando g_gameFfbMode (ver
    // FOC_RUN, guardado por canEnterGameFfb/shouldDisarmGameFfb) — este
    // gate segue morto de propósito.
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

    // PID State report (Input, RID 0x02) — confirma ao host que os atuadores estão
    // habilitados. Enviado como heartbeat LENTO (10Hz) e SÓ na sobra do endpoint (depois
    // do joystick, que tem prioridade absoluta acima). NÃO usa mais gatilho por-FFB-report:
    // aquilo disparava a cada força constante e roubava TODAS as vagas do endpoint IN,
    // matando o input do volante no jogo. Ver o bloco de prioridade do joystick acima.
    static uint32_t lastPidState = 0;
    if (g_hid->ready() && (now - lastPidState >= 100))
    {
        lastPidState = now;
        g_pidStateDirty = false;
        uint8_t st = buildPidStateByte(false /*devicePaused*/, true /*actuatorsEnabled*/,
                                       true /*safetySwitch*/, false /*actuatorOverride*/,
                                       true /*actuatorPower*/);
        UsbBase::sendReport(RID_PID_STATE, &st, 1);
        g_ffbMon[14]++;   // instrumentação: PID State enviado
    }

    TinyUSBDevice.task();   // PERF/400Hz: 3ª drenada do USB por loop (ver comentário acima)
}
