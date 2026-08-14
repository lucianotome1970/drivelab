// firmware-base — ffb_task (STAGE 3b): o laço de FFB de 1 kHz. ESTADO VALIDADO (FFB testado
// no ACC 10+ vezes: força coerente + damper + auto-arme nativo do ODrive, sem runaway).
//
// Cada tick: avança o relógio do modelo, transmite a direção pro host (joystick HID) e, com o
// motor ARMADO, pede o torque do ffb_model (força do jogo + efeitos + mola/damper/endstop) e o
// escreve na ponte. O ODrive faz a FOC/proteção E o auto-arme de boot (startup_closed_loop na NVM).
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#include <stdint.h>
#include <math.h>          // fabsf — guarda de coerência do ângulo elétrico
#include <cmsis_os.h>
#include "stm32f4xx.h"   // NVIC_SystemReset (reboot pelo app)
#include "motor_link.h"
#include "ffb_model.h"
#include "peak_tracker.h"   // picos de corrente (só leitura)
#include "blackbox.h"          // caixa-preta: causa do ultimo reset (lida no boot)
#include "brake_meter.h"          // zerar o medidor do chopper no arme (ver autoscale, abaixo)
#include "overtravel_guard.h"      // o volante nao pode estar alem do curso (guarda por POSICAO)
#include "wheel_center.h"         // o zero do volante (batente nasce centrado) — ver o header
#include "watchdog.h"             // a base reinicia sozinha em vez de congelar — ver o header
extern BrakeMeter g_brake_meter;  // definido em vendor/odrive-fw/MotorControl/low_level.cpp

// Definido em ffb_hid.cpp: joystick (direção pro jogo).
extern "C" int hid_send_joystick(void);      // joystick (direção pro jogo) — PRIORIDADE
extern "C" void hid_usb_watchdog(uint32_t nowTick);   // destrava o EP IN se o host parar de aceitar
extern "C" int32_t a0_peek_motor_enable(void);   // trava lida da NVM antes do eixo iniciar
extern "C" int a0_service(uint32_t nowMs);   // canal A0 do app (0x16 resposta / 0x21 telemetria) — na sobra
extern "C" bool a0_reboot_pending(void);     // CMD_REBOOT: desarmar e resetar o MCU
extern "C" bool a0_dfu_pending(void);        // CMD_DFU: desarmar e saltar pro bootloader
extern "C" int32_t a0_get_setting(uint8_t id);  // valor atual de um setting (guarda de curso excedido)
extern "C" bool a0_save_pending(void);       // CMD_SAVE pediu persistir os settings na FFB_NVM?
extern "C" bool a0_commit_save(void);        // empacota + grava na flash (chamar SÓ com motor IDLE)

// Diagnóstico do eixo, lido por SWD (SÓ leitura — não age no motor).
volatile int32_t g_axis_dbg[7] = {0};   // [0]armed [1]state [2]axis_err [3]motor_err [4]enc_err [5]pos*1e3 [6]vel*1e3
// Auto-arme com retry: 1 = após a cal, se ficar em IDLE (mesmo com erro), limpa e pede closed loop.
// A cal do motor às vezes falha (CONTROL_DEADLINE_MISSED, timing); re-tentar fecha o arme. Zerar = seguro.
volatile int32_t g_arm_gate = 1;
// 1 = a calibração de offset deste boot está TRANCADA (pre_calibrated + índice reancorando).
// Só leitura, para diagnóstico por SWD: se ficar 0 depois do boot, a cal não concluiu.
volatile int32_t g_cal_locked = 0;

// Trava de bring-up pedida na bancada (2026-08-09): a base sobe DESARMADA e só responde a comando
// depois que o usuário confere os campos de hardware e liga "Ativar motor" (setting 45). Um firmware
// recém-gravado não sabe qual motor/encoder está do outro lado; armar no boot com dados errados
// esquenta o motor ou o faz disparar. Desligar desarma NA HORA — serve de parada de emergência.
// Escrito por a0_apply_settings() (canal do app), lido aqui no laço de 1 kHz.
volatile int32_t g_motor_enable = 0;
// Config real (NVM) lida por SWD: [0]startup_flags [1]precal_flags [2]motor_R_uohm [3]motor_L_nH
volatile int32_t g_cfg_dbg[4] = {0};

// Referenciado por low_level.cpp (core patchado): vbus_voltage = adc * este scale.
// Divisor 19:1 do MKS ODRIVE-S (ODrive genuíno usa 11:1).
volatile float g_vbus_voltage_scale = 3.3f * 19.0f / 4096.0f;

// 1 = os limites de bus já foram dimensionados pela fonte MEDIDA (chopper liberado);
// 0 = ainda não (sem fonte no boot → chopper mudo). Legível por SWD para diagnóstico.
volatile int32_t g_bus_autoscaled = 0;

// LATCH DA PRIMEIRA FALHA — só leitura por SWD, não age no motor.
//
// Por que existe: em 2026-08-06, depois de 3 voltas limpas, o FFB caiu com
// axis_err=CONTROLLER_FAILED. O "porquê" mora em controller_.error_ — e quando fomos ler, já
// estava ZERADO: o auto-arme roda clear_errors() 68 ms depois e apaga tudo. Ficamos com "o
// controlador falhou" e três candidatos possíveis (SPINOUT_DETECTED, INVALID_ESTIMATE, OVERSPEED)
// sem meio de distinguir. Aqui guardamos o estado ANTES do clear, e só a PRIMEIRA vez: as falhas
// seguintes costumam ser eco da primeira, e sobrescrever perderia justamente a original.
//
// As duas potências importam tanto quanto o código de erro: se no instante da falha a mecânica
// estiver bem negativa e a elétrica positiva, foi a detecção de spinout do ODrive — que num volante
// FFB dispara em condição NORMAL de uso (segurar o volante contra a força é exatamente "frear
// consumindo corrente"), e aí o conserto é afrouxar/desligar o detector, não caçar bug.
volatile int32_t g_fail_dbg[8] = {0};
// [0]ocorrências [1]controller_err [2]axis_err [3]motor_err [4]enc_err
// [5]mech_power*1000 (W) [6]elec_power*1000 (W) [7]vbus*1000 (V)

// ============================================================================================
// GUARDA DE COERÊNCIA DO ÂNGULO ELÉTRICO
// ============================================================================================
// O QUE PROTEGE: uma calibração pode CONCLUIR (is_ready=1, erro zero, motor armado) e mesmo
// assim ter gravado um ângulo elétrico errado — por fases religadas em outra ordem, encoder
// remontado, offset mal medido. O ODrive não questiona o próprio resultado: ele arma e aplica
// corrente. Com o ângulo errado essa corrente não vira torque, vira CALOR — e, com o volante
// livre, vira giro descontrolado.
//
// MEDIDO NA BANCADA (2026-08-07), com o mesmo firmware e o mesmo hardware:
//   calibração boa → Iq = 0,10 A com o volante parado
//   calibração ruim → Iq = 18,0 A TRAVADO, ~98 W de puro calor, e 34 voltas sozinho em 25 s
// 180× de diferença. E em nenhum dos casos ruins o firmware acusou erro: axis=0, motor=0,
// nenhum SPINOUT_DETECTED. Do ponto de vista dos flags, estava tudo bem.
//
// COMO DETECTA — por INCOERÊNCIA FÍSICA, não por limiar de corrente:
//   se NÃO estamos pedindo torque  E  o volante NÃO está se movendo,
//   então NÃO PODE haver corrente. Se houver, o ângulo está errado.
// Não comanda nada, não gira o motor, não depende de calibrar de novo: só observa.
//
// POR QUE AS TRÊS CONDIÇÕES JUNTAS (nenhuma sozinha serve):
//   - torque comandado alto é legítimo (o jogo pedindo força, ou o batente segurando) → não conta
//   - volante em movimento tem corrente legítima (acelerar a inércia, damper) → não conta
//   - corrente alta só é anômala quando as outras duas dizem que ela não deveria existir
// Por isso segurar o volante contra a força NÃO dispara: ali o torque comandado é alto.
//
// AÇÃO: desarma e TRAVA o auto-arme (g_arm_gate=0). Travar é essencial — sem isso o retry
// re-arma na mesma calibração ruim e o ciclo recomeça (medimos 300 tentativas seguidas num caso
// assim). Sai do estado só com power-cycle, que é justamente quando uma nova calibração roda.
static constexpr float    kGuardIqA        = 2.0f;   // 8× o ruído (0,25 A), 9× abaixo dos 18 A vistos
static constexpr float    kGuardTorqueNm   = 0.2f;   // abaixo disto = "não estamos pedindo torque"
static constexpr float    kGuardVelTurnsS  = 0.1f;   // abaixo disto = "parado"
static constexpr uint16_t kGuardMs         = 300;    // persistência: ignora transiente, pega o travado
volatile int32_t g_guard_trip    = 0;   // 1 = disparou (legível por SWD e telemetria)
volatile int32_t g_guard_iq_ma   = 0;   // corrente no instante do disparo, em mA — a prova
volatile int32_t g_guard_bad_ms  = 0;   // quanto tempo incoerente agora (0 = saudável)

// ============================================================================================
// GUARDA DE SOBREVELOCIDADE — o volante NÃO PODE girar sozinho
// ============================================================================================
// Esta é a proteção de último recurso, e ela existe por um princípio: seja qual for a falha, um
// volante direct-drive com um aro pesado NÃO PODE disparar. Ponto. Todas as outras guardas
// dependem de entender a causa; esta não depende de causa nenhuma — só de constatar que ninguém
// humano gira um volante nessa velocidade.
//
// POR QUE NÃO EXISTIA: o ODrive tem `vel_limit`, que gera OVERSPEED e desarma. Nós o DESLIGAMOS
// de propósito (`enable_vel_limit = false`), porque com ele ligado o controlador corta torque ao
// girar rápido e o feel fica ruim. A troca era razoável, mas junto foi embora a única proteção
// contra o motor acelerar sozinho — e em 2026-08-07 medimos 34 voltas em 25 s, ainda acelerando,
// com o firmware reportando "tudo bem" (axis=0, motor=0). O usuário apontou o óbvio: o erro pode
// acontecer, mas o disparo não pode.
//
// POR QUE ESTA GUARDA NÃO ESTRAGA O FEEL: ela NÃO limita nem corta torque — só observa. Enquanto a
// velocidade for humana, ela é inerte, então não há clamp cortando força numa correção de traseira.
//
// LIMIAR: um piloto gira talvez 2-3 voltas/s em pico numa correção rápida. Um runaway com 10 Nm
// passa disso e continua acelerando. 5 voltas/s deixa margem confortável para o uso e pega o
// disparo bem antes de ele ficar perigoso. A persistência evita disparar num tranco isolado.
static constexpr float    kOverspeedTurnsS = 5.0f;   // ~1800 °/s — bem acima de qualquer giro humano
static constexpr uint16_t kOverspeedMs     = 150;    // sustentado, não pico
volatile int32_t g_overspeed_trip     = 0;   // 1 = disparou
volatile int32_t g_overspeed_vel_mts  = 0;   // velocidade no disparo (milli-turns/s) — a prova
volatile int32_t g_overspeed_bad_ms   = 0;   // há quanto tempo acima do limiar (0 = normal)

// ============================================================================================
// GUARDA DE CURSO EXCEDIDO — ver inc/overtravel_guard.h para o porque e a maquina de estados.
// Complementa as duas de cima: elas olham velocidade e corrente, esta olha POSICAO — e e a unica
// que pega o disparo LENTO, que nao cruza limiar de velocidade nenhum.
// ============================================================================================
static OvertravelState s_overtravel;
static OvertravelCfg   s_overtravel_cfg;
static uint8_t         s_overtravel_ready = 0;
// Freio do disparo: amortecimento puro, proporcional a velocidade. Teto BAIXO de proposito — o
// objetivo e parar sem tranco, nao construir outra parede. 0,6 Nm por rad/s satura em 5 Nm perto
// de 8 rad/s, que ja e giro de disparo.
static constexpr float kOtBrakeNmPerRadS = 0.6f;
static constexpr float kOtBrakeMaxNm     = 5.0f;
volatile int32_t g_overtravel_trip    = 0;   // 1 = disparou (legivel por SWD)
volatile int32_t g_overtravel_pos_mrad = 0;  // posicao no disparo — a prova
volatile int32_t g_overtravel_trips   = 0;   // quantas vezes neste boot

// Medidores do monitor (SÓ LEITURA — não entram em nenhuma decisão de controle).
// Ficam aqui, no laço de 1 kHz, e NÃO na ISR de 8 kHz: a lição de 2026-08-06 é que
// mexer perto do ADC/laço de corrente quebra a FOC. Aqui só se lê valor já pronto.
int32_t g_encoder_cfg_changed = 0;   // 1 = config de encoder mudou neste boot (cal invalidada)
PeakTracker g_current_peak_ma = {0, 0};   // picos + e − da corrente, em mA, desde o boot
uint8_t     g_clip_peak       = 0;        // pico de clipping da sessão (0-255)
// As duas PARCELAS do pico, pelas mesmas razões da separação ao vivo (ver ffb_model.cpp). O pico da
// sessão é o número que se olha DEPOIS de sair da pista — o valor ao vivo dura 500 ms e some antes
// de quem está pilotando conseguir ler. Se ele não disser a origem, a decomposição não serve para
// nada na hora de decidir o que ajustar, que é exatamente quando ela é consultada.
uint8_t     g_clip_peak_game  = 0;
uint8_t     g_clip_peak_base  = 0;

static void ffb_thread(void*) {
    uint32_t tick = osKernelSysTick();   // base absoluta p/ osDelayUntil (1kHz sem drift)
    uint32_t n = 0;

    // Arma o watchdog AQUI, dentro da tarefa que vai alimentá-lo, e não no boot.
    //
    // Assim ele nasce junto de quem tem a obrigação de mantê-lo vivo: entre armar e a primeira
    // alimentação não existe nenhuma etapa longa de inicialização que pudesse estourar a janela e
    // reiniciar a base logo ao ligar — que seria a pior estreia possível para uma proteção.
    watchdog_init();

    for (;;) {
        ffb_model_advance_clock(1);                     // relógio do modelo (1 ms/tick)

        // Diagnóstico do eixo (só leitura por SWD)
        g_axis_dbg[0] = motor_link_motor_is_armed();
        g_axis_dbg[1] = motor_link_axis_state();
        g_axis_dbg[2] = (int32_t)motor_link_axis_error();
        g_axis_dbg[3] = (int32_t)motor_link_motor_error();
        g_axis_dbg[4] = (int32_t)motor_link_encoder_error();
        g_axis_dbg[5] = (int32_t)(motor_link_get_pos_turns() * 1000.0f);
        g_axis_dbg[6] = (int32_t)(motor_link_get_vel_estimate() * 1000.0f);

        // Medidores do monitor (só leitura, a 1 kHz — pega transiente que a telemetria
        // de 50 Hz perderia entre amostras).
        peak_tracker_update(&g_current_peak_ma,
                            (int32_t)(motor_link_get_iq_measured() * 1000.0f));
        // As parcelas são fotografadas NO INSTANTE do pico do total — não é o pico de cada uma.
        // Acompanhar cada máximo em separado dava números que não fecham: o pior momento do jogo
        // acontece numa curva e o da base em outra, então a tela mostrava "22 % (jogo 20 · base 6)"
        // e 20+6 não dá 22. Matematicamente correto (max(a+b) ≤ max a + max b) e ilegível na prática.
        // Assim a decomposição descreve UM instante — o pior — e a soma fecha.
        { const uint8_t c = ffb_model_get_clipping();
          if (c > g_clip_peak) {
              g_clip_peak      = c;
              g_clip_peak_game = ffb_model_get_clipping_game();
              g_clip_peak_base = ffb_model_get_clipping_base();
          } }
        g_cfg_dbg[0] = motor_link_startup_flags();
        g_cfg_dbg[1] = motor_link_precal_flags();
        g_cfg_dbg[2] = motor_link_motor_R_uohm();
        g_cfg_dbg[3] = motor_link_motor_L_nH();

        // Reiniciar a base pelo app (botão da aba Hardware). Desarma ANTES de resetar: um reset com
        // as fases energizadas deixa o estágio de potência num estado indefinido. Só depois que o
        // motor está em IDLE é que o MCU reseta — o USB cai e volta sozinho.
        // Entrar em DFU pelo app: mesma sequência do reboot — DESARMA primeiro. O salto reseta o
        // MCU, e resetar com as fases energizadas deixa o estágio de potência em estado indefinido.
        // Só com o motor em IDLE é que pedimos o salto (dfu_request_jump não retorna).
        if (a0_dfu_pending()) {
            g_arm_gate = 0;
            motor_link_set_input_torque(0.0f);
            if (motor_link_motor_is_armed()) {
                motor_link_request_idle();
            } else {
                motor_link_enter_dfu();               // não retorna
            }
        }

        if (a0_reboot_pending()) {
            g_arm_gate = 0;                              // não deixa o auto-arme brigar com a saída
            motor_link_set_input_torque(0.0f);
            if (motor_link_motor_is_armed()) {
                motor_link_request_idle();
            } else {
                NVIC_SystemReset();                      // não retorna
            }
        }

        // Save de settings na FFB_NVM (deferido do CMD_SAVE): o erase/program CONGELA a CPU ~centenas de
        // ms (F405 single-bank) → precisa do motor IDLE, senão a ISR do controle perde deadline. Suspende
        // o auto-arme, desarma, grava, e re-permite o arme (o app re-liga a força). ⚠️ A força cai ~1s e
        // o motor re-arma (pode re-calibrar o offset do encoder) — validar o feel na bancada.
        if (a0_save_pending()) {
            g_arm_gate = 0;                              // suspende o auto-arme (senão re-arma na hora)
            if (motor_link_motor_is_armed()) {
                motor_link_request_idle();            // desarma p/ a flash não estourar deadline
            } else {
                motor_link_set_input_torque(0.0f);    // torque zero antes de congelar a CPU
                a0_commit_save();                        // erase+program (bloqueia, mas motor OFF = ok)
                g_arm_gate = 1;                          // re-permite o arme
            }
        }

        // Dimensiona os limites de bus pela fonte MEDIDA e só então libera o chopper.
        // Roda UMA vez, e SÓ COM O MOTOR JÁ ARMADO (estado 8). Duas armadilhas motivaram isso, ambas
        // pagas na bancada em 2026-08-05:
        //  1) cedo demais: no boot o ADC ainda não leu e `vbus_voltage` vale o inicializador 12.0f de
        //     low_level.cpp → dimensionava p/ uma fonte imaginária de 12 V (rampa 14/16 com a fonte
        //     real em 24 V);
        //  2) no meio da CALIBRAÇÃO: ao ligar enable_brake_resistor, o apply_pwm_timings (8 kHz) vê
        //     (enable && !armed) e derruba o motor com BRAKE_RESISTOR_DISARMED → a cal de offset
        //     aborta ("gira um pouco pra um lado e para" → UNKNOWN_PHASE_ESTIMATE).
        // Com o motor armado, a cal já terminou e o vbus é leitura real e estável. (O arme do
        // resistor em si é feito sem janela dentro da função — ver motor_link.cpp.)
        if (!g_bus_autoscaled && g_axis_dbg[1] == 8) {
            g_bus_autoscaled = motor_link_autoscale_bus_limits();
            // Zera o medidor do chopper AQUI: a calibração de offset do boot GIRA o motor, e girar
            // gera regeneração — acionamento legítimo, mas que não é uso. Sem isto a base mostra
            // ~935 acionamentos / 0,8 J assim que liga, e o usuário lê "o chopper está trabalhando
            // parado" (constatado na bancada 2026-08-06). Os contadores passam a medir a SESSÃO:
            // do arme em diante. Zero depois de dirigir continua sendo diagnóstico de resistor
            // desconectado ou rampa mal dimensionada, que é a razão de a feature existir.
            brake_meter_init(&g_brake_meter);
        }

        // GUARDA DE SOBREVELOCIDADE — vem ANTES de tudo: é a proteção que não pode falhar.
        // Só observa a velocidade; não corta torque, não limita nada enquanto o giro for humano.
        if (g_axis_dbg[0] && !g_overspeed_trip) {
            static uint16_t s_fast_ms = 0;
            const float vel = fabsf(motor_link_get_vel_estimate());   // turns/s
            if (vel > kOverspeedTurnsS) {
                if (++s_fast_ms >= kOverspeedMs) {
                    g_overspeed_vel_mts = (int32_t)(vel * 1000.0f);   // a prova, ANTES de desarmar
                    g_overspeed_trip    = 1;
                    g_arm_gate          = 0;      // não re-armar: a causa continua lá
                    motor_link_set_input_torque(0.0f);
                    motor_link_request_idle();
                }
            } else {
                s_fast_ms = 0;
            }
            g_overspeed_bad_ms = s_fast_ms;
        }

        // Guarda de coerência do ângulo elétrico (ver o bloco de comentário lá em cima).
        // Roda no laço de 1 kHz, SÓ LEITURA até decidir desarmar — não toca no caminho do torque.
        if (g_axis_dbg[0] && !g_guard_trip) {          // só com o motor armado, e uma vez só
            static uint16_t s_bad_ms = 0;
            const float iq  = fabsf(motor_link_get_iq_measured());
            const float trq = fabsf(motor_link_get_input_torque());
            const float vel = fabsf(motor_link_get_vel_estimate());
            if (iq > kGuardIqA && trq < kGuardTorqueNm && vel < kGuardVelTurnsS) {
                if (++s_bad_ms >= kGuardMs) {
                    g_guard_iq_ma = (int32_t)(iq * 1000.0f);   // guarda a prova ANTES de desarmar
                    g_guard_trip  = 1;
                    g_arm_gate    = 0;                 // trava o auto-arme: não re-armar a mesma cal ruim
                    motor_link_request_idle();
                }
            } else {
                s_bad_ms = 0;                          // qualquer tick coerente zera a contagem
            }
            g_guard_bad_ms = s_bad_ms;
        }

        // Auto-arme com retry ESPAÇADO + timeout de segurança.
        //
        // Era: 1 tentativa por ENTRADA em IDLE (`!s_was_idle`), teto 15 → uma vez preso em IDLE
        // nunca mais tentava, e 15 falhas seguidas desistiam PARA SEMPRE (FFB morto, sem aviso).
        // Foi o que travou o teste do brake chopper em 2026-08-04: a sobretensão desarma o motor E o
        // brake resistor juntos (ODrive::disarm_with_error → safety_critical_disarm_brake_resistor),
        // aí (enable && !armed) derruba toda tentativa de arme com BRAKE_RESISTOR_DISARMED até
        // esgotar o teto. Agora clear_errors() re-arma o brake (ver motor_link.cpp), então o
        // retry PRECISA insistir para a recuperação acontecer.
        //
        // Agora: tenta enquanto estiver em IDLE, mas ESPAÇADO — 50 ms nas primeiras tentativas e
        // 250 ms depois de 10 falhas seguidas. O espaçamento importa: re-armar a cada tick (1 ms)
        // contra uma falha persistente vira churn arma/desarma (medimos ~140 Hz por SWD), que o
        // usuário sente como "tec". O contador só zera quando o motor SUSTENTA o arme por 1 s —
        // assim uma recuperação real limpa o histórico, mas um ciclo de falha não se disfarça.
        {
            static uint32_t s_cal_ticks    = 0;
            static int      s_arm_attempts = 0;
            static int      s_cal_attempts = 0;   // calibrações pedidas (teto próprio — ver abaixo)
            static bool     s_cal_locked   = false; // já calibrou neste boot? então NUNCA recalibrar
            static uint32_t s_next_try     = 0;   // tick da próxima tentativa (backoff)
            static uint32_t s_armed_ticks  = 0;   // há quanto tempo está armado
            const int st = g_axis_dbg[1];
            const bool in_cal = (st == 3 || st == 4 || st == 6 || st == 7);

            // Transição "acabou de calibrar": tranca o resultado e liga a reancoragem pelo índice.
            // A partir daqui o offset é DEFINITIVO neste boot e cada passagem pelo Z re-sincroniza a
            // contagem (uma vez por volta), corrigindo pulso perdido antes que ele vire deslocamento
            // permanente da referência. Ver motor_link_lock_calibration().
            if (!s_cal_locked && !in_cal && motor_link_encoder_is_ready()) {
                motor_link_lock_calibration();
                s_cal_locked  = true;
                g_cal_locked  = 1;      // legível por SWD
            }

            if (in_cal) {
                // TETO DE TEMPO DA CALIBRAÇÃO — 12 s → 40 s (2026-08-08). Mudança DEFENSIVA.
                //
                // A conta que motivou: calib_scan_distance=16π rad elétricos a calib_scan_omega=4π
                // rad/s dá 4 s POR varredura, e são DUAS (ida e volta), mais o lock-in que assenta o
                // rotor antes de começar. Com carga alta (aro montado) isso encosta em 12 s, e o teto
                // cortaria a calibração PELA METADE — que é exatamente como se manifesta na bancada:
                // "o motor gira só para um lado e para".
                //
                // ⚠️ NÃO é a explicação do caso que estávamos investigando: ali o motor estava SEM o
                // aro, inércia baixa, e a calibração não teria por que passar de 12 s. Aquele sintoma
                // segue em aberto (o motor_err dizia UNKNOWN_PHASE_ESTIMATE, nunca DRV_FAULT, então
                // o driver não estava em falta). Mesmo assim o teto sobe: 12 s era apertado demais
                // para um procedimento que legitimamente leva ~10 s com carga, e um teto que corta
                // pela metade produz uma falha que PARECE hardware intermitente.
                //
                // 40 s continua sendo teto de segurança real: uma cal travada não fica energizando
                // o motor indefinidamente.
                if (++s_cal_ticks > 40000) motor_link_request_idle();
            } else {
                s_cal_ticks = 0;
                if (st == 8 && g_motor_enable != 1) {
                    // "Ativar motor" foi DESLIGADO com a base armada → desarma imediatamente. É a
                    // metade que faz a trava valer como parada de emergência: se o ajuste de hardware
                    // saiu errado, o usuário desliga e o motor para, sem reboot e sem tirar da tomada.
                    motor_link_set_input_torque(0.0f);
                    motor_link_request_idle();
                    s_arm_attempts = 0;
                    s_armed_ticks  = 0;
                } else if (st == 8) {
                    if (s_armed_ticks <= 1000 && ++s_armed_ticks > 1000) s_arm_attempts = 0;
                } else if (st == 1 && g_arm_gate == 1 && g_motor_enable == 1) {
                    s_armed_ticks = 0;
                    // (int32_t) na diferença = seguro no wrap do contador de 1 kHz
                    if ((int32_t)(n - s_next_try) >= 0 && s_arm_attempts < 300) {
                        // ANTES do clear: fotografa a falha, senão ela se perde (ver g_fail_dbg).
                        if (g_fail_dbg[0] == 0) {
                            g_fail_dbg[1] = (int32_t)motor_link_controller_error();
                            g_fail_dbg[2] = g_axis_dbg[2];   // axis_err
                            g_fail_dbg[3] = g_axis_dbg[3];   // motor_err
                            g_fail_dbg[4] = g_axis_dbg[4];   // enc_err
                            g_fail_dbg[5] = (int32_t)(motor_link_get_mech_power() * 1000.0f);
                            g_fail_dbg[6] = (int32_t)(motor_link_get_elec_power() * 1000.0f);
                            g_fail_dbg[7] = (int32_t)(motor_link_get_vbus() * 1000.0f);
                        }
                        g_fail_dbg[0]++;
                        motor_link_clear_errors();      // limpa erros + RE-ARMA o brake resistor
                        // Com a trava de bring-up (setting 45) a calibração NÃO roda no boot: o
                        // encoder chega aqui sem referência e o desvio abaixo é o que dá a partida
                        // nela quando o usuário ativa o motor. Os dois tetos continuam valendo.

                        // CALIBRAR ANTES DE ARMAR — corrigido em 2026-08-07.
                        //
                        // Antes pedíamos CLOSED_LOOP sempre. Mas malha fechada exige estimativa de
                        // fase, que só existe depois da calibração de offset. O ODrive roda essa cal
                        // UMA vez no boot (startup_encoder_offset_calibration) e, se ela falhar, mais
                        // ninguém pede — o auto-arme passava a bater 300 vezes contra um estado
                        // impossível, e o motor NEM SE MEXIA. Foi o que confundiu a bancada a tarde
                        // inteira: parecia "não consegue calibrar" quando era "não está tentando".
                        //
                        // O retry espaçado (50/250 ms) já existia e serve igual aqui: cada tentativa
                        // agora é uma CALIBRAÇÃO nova, que é justamente o que uma falha transitória
                        // (cogging, inércia do aro) precisa para acertar na segunda ou terceira.
                        // ⚠️ Teto SEPARADO para calibrações: cada uma energiza o motor por ~5 s. As
                        // 300 tentativas de ARME são baratas (falham em ms); 300 CALIBRAÇÕES seriam
                        // 25 minutos girando o motor — aquecimento por insistência, sem ninguém
                        // olhando. 8 tentativas cobrem a falha transitória e param antes de cozinhar.
                        // ⚠️ UMA CALIBRAÇÃO POR BOOT (2026-08-08) — s_cal_locked.
                        //
                        // Depois que a cal de offset conclui UMA vez, NUNCA mais pedir outra neste
                        // boot. Antes, qualquer queda para IDLE com is_ready=false disparava uma
                        // calibração NOVA — e o motor cai justamente quando o batente satura, ou
                        // seja, com o volante ENCOSTADO NO FIM DE CURSO. A cal de offset precisa
                        // girar o rotor livremente para varrer; presa contra o batente ela varre
                        // torto e grava um offset ruim POR CIMA do que estava bom.
                        //
                        // É exatamente o relato da bancada: "corrijo o lado do batente, giro para o
                        // outro, e o lado que estava perfeito fica estragado". Não era erro fixo de
                        // ângulo (as medições descartaram: CPR 4000 ✓, 15 pares ✓, índice sem
                        // glitch ✓) — era a REFERÊNCIA sendo reescrita durante o uso, pelo próprio
                        // firmware, na pior condição possível.
                        //
                        // Se o motor não armar mesmo com a cal boa, o certo é falhar e acusar, não
                        // recalibrar às cegas: recalibrar não conserta arme, e estraga o que funciona.
                        if (!motor_link_encoder_is_ready() && !s_cal_locked) {
                            if (s_cal_attempts < 8) {
                                motor_link_request_encoder_calibration();   // estado 7
                                s_cal_attempts++;
                            }
                        } else {
                            s_cal_attempts = 0;                // calibrou: zera o histórico
                            motor_link_request_closed_loop();               // estado 8
                        }
                        s_arm_attempts++;
                        s_next_try = n + (s_arm_attempts < 10 ? 50u : 250u);
                    }
                }
            }
        }

        // Disciplina de envio (1 report por janela de EP, como o firmware-base): JOYSTICK tem
        // prioridade (o jogo precisa da direção); o canal A0 do app pega a SOBRA (janela 3 de 4).
        // Se A0 não tem nada a enviar, o joystick usa a janela.
        if ((n & 3) == 3) { if (!a0_service(n)) hid_send_joystick(); }
        else              { hid_send_joystick(); }
        hid_usb_watchdog(n);   // EP IN travado > 2 s → re-enumera sozinho (eixo congelado ao sair do jogo)
        n++;

        if (g_axis_dbg[0]) {   // só age com o motor ARMADO (CLOSED_LOOP)
            // Primeiro arme do boot: onde o volante está AGORA vira o centro. Sem isto o batente
            // nasce onde a contagem crua do encoder por acaso estiver — foi o que travou o motor
            // em -10 Nm / 18 A parado na bancada de 2026-08-07. Ver inc/wheel_center.h.
            if (!wheel_center_is_set()) wheel_center_capture();
            const float pos = wheel_center_pos_turns();          // turns RELATIVOS ao centro
            const float vel = motor_link_get_vel_estimate();  // turns/s (derivada: o zero não a afeta)

            // GUARDA DE CURSO EXCEDIDO. Roda ANTES de aplicar o torque do pipeline, porque a
            // decisão dela é sobre QUAL torque aplicar — não é observador como as outras duas.
            // A ação escolhida pelo usuário (travar ou re-armar) é lida a cada tick: mudar o
            // ajuste no app passa a valer sem reiniciar.
            if (!s_overtravel_ready) { overtravel_init(&s_overtravel);
                                       s_overtravel_cfg = overtravel_default_cfg();
                                       s_overtravel_ready = 1; }
            s_overtravel_cfg.mode = (uint8_t)(a0_get_setting(57) != 0 ? OT_MODE_REARM : OT_MODE_LOCK);

            const float posRad = pos * 6.2831853f;
            const float velRad = vel * 6.2831853f;
            const OvertravelAction ota = overtravel_update(&s_overtravel, &s_overtravel_cfg,
                                                           posRad, ffb_model_dor_half_rad(),
                                                           velRad, 1);
            g_overtravel_pos_mrad = s_overtravel.last_pos_mrad;
            g_overtravel_trips    = s_overtravel.trips;

            if (ota == OT_ACT_NORMAL) {
                motor_link_set_input_torque(ffb_model_compute_torque(pos, vel));
            } else if (ota == OT_ACT_BRAKE) {
                // Amortecimento puro: nada do jogo entra. Zera o estado transitório junto, senão o
                // limitador de variação atrapalharia o próprio freio.
                ffb_model_reset_transient();
                float t = -kOtBrakeNmPerRadS * velRad;
                if (t >  kOtBrakeMaxNm) t =  kOtBrakeMaxNm;
                if (t < -kOtBrakeMaxNm) t = -kOtBrakeMaxNm;
                motor_link_set_input_torque(t);
            } else {   // DISARM ou HOLD
                g_overtravel_trip = 1;
                motor_link_set_input_torque(0.0f);
                if (ota == OT_ACT_DISARM) {
                    // Trava o auto-arme SÓ quando a guarda decidiu travar. No modo re-armar ela
                    // mesma devolve o controle assim que o volante voltar ao curso e parar.
                    if (s_overtravel.state == OT_ST_LOCKED) g_arm_gate = 0;
                    motor_link_request_idle();
                }
            }
        } else {
            motor_link_set_input_torque(0.0f);
            ffb_model_reset_clipping();   // desarmado não clipa — não deixar o último valor congelado na tela
            // Estado transitório zerado ENQUANTO desarmado, e não na borda do arme. É de
            // propósito: chamar todo tick é idempotente e não depende de detectar transição
            // — detecção de borda erra quando o estado muda por um caminho que ninguém
            // lembrou (guarda que desarma, save, DFU). Custo: três atribuições por ms.
            ffb_model_reset_transient();
        }
        // WATCHDOG — alimentado AQUI, no fim do laço de 1 kHz, e não num timer.
        //
        // A diferença importa: um timer de hardware continuaria alimentando com o resto do
        // firmware morto, e o watchdog viraria enfeite. Este ponto só é alcançado se o
        // escalonador está rodando, esta tarefa acordou e a volta inteira executou — que é o
        // que "sistema saudável" significa na prática. Nos dois travamentos que diagnosticamos
        // (tempestade de IRQ do USB e breakpoint do TinyUSB) o laço parava exatamente aqui.
        watchdog_feed();
        osDelayUntil(&tick, 1);   // 1 kHz absoluto (sem drift)
    }
}

extern "C" void ffb_storage_preload(void) {
    // Caixa-preta: lê a causa do último reset do RCC->CSR e LIMPA as flags. Tem de rodar antes de
    // qualquer coisa limpá-las, e este é o primeiro hook nosso no boot. Só leitura de registrador —
    // não toca motor, PWM nem config. Ver inc/blackbox.h.
    blackbox_init();

    // SEM APITO + arme confiável: pula a medição do motor no boot (usa o R/L já guardado,
    // motor.pre_calibrated=TRUE). Roda ANTES do eixo iniciar (main.cpp:544 < :604). Mantém a
    // cal de offset do encoder (movimento) + o auto-arme nativo (startup_closed_loop na NVM).
    // ⚠️ BRING-UP DE PLACA NOVA (2026-08-03): a placa nova tem cal de fábrica de OUTRO motor. Em vez de
    // pular a cal (skip_motor_cal), fazemos a cal COMPLETA do nosso motor (pole_pairs=15, cpr=4000,
    // mede R/L, offset do encoder) com corrente segura. Já inclui o disable_brake_resistor. Testa o DRV
    // limpo. Ao voltar pra placa antiga (NVM já calibrada), trocar de volta por skip+disable.
    motor_link_newboard_bringup();
    // DEPOIS do bringup (ele fixa calibration_current=3A): sobe p/ 5A + afrouxa o calib_range.
    // 3A não vence o cogging do hoverboard (15 pares) → a varredura de offset gira ~180° e PARA,
    // deixando UNKNOWN_PHASE_ESTIMATE. A função existia desde 2026-08-06 mas nunca era chamada.
    motor_link_relax_calibration();

    // Aplica o encoder ESCOLHIDO NO APP por cima dos valores de fabrica do bring-up. Com os
    // settings no padrao nada muda; se o usuario configurou outro encoder, a calibracao anterior
    // deixa de valer e a base pede recalibracao (ver motor_link_apply_encoder_settings).
    g_encoder_cfg_changed = motor_link_apply_encoder_settings();

    // Idem para o motor: pares de polos e corrente de calibracao vinham cravados. Com os defaults
    // (15 e 5 A) nada muda; com outro motor configurado, a calibracao anterior deixa de valer.
    if (motor_link_apply_motor_settings()) g_encoder_cfg_changed = 1;

    // Protecao termica dos FETs: o limitador do ODrive ja rodava (100/120 °C de fabrica), mas o
    // nosso campo nunca chegava nele. Nao invalida calibracao — so mexe em quanto calor a placa
    // aceita antes de comecar a cortar corrente.
    motor_link_apply_thermal_settings();

    // TRAVA DE BRING-UP TAMBÉM SOBRE A CALIBRAÇÃO. Até aqui a trava segurava só o ARME: com ela em
    // zero a base subia desarmada, mas a varredura de offset do encoder rodava assim mesmo — e ela
    // JÁ INJETA CORRENTE, gira o motor e esquenta. Numa placa recém-gravada com pole_pairs/CPR/
    // variante errados, que é exatamente o caso que a trava existe para cobrir, o estrago acontecia
    // antes de qualquer confirmação do usuário.
    // Com a trava em zero a base agora sobe SEM TOCAR NO MOTOR. Quem dá a partida na sequência
    // (calibra → arma) é o "Ativar motor" do app, tratado no laço de 1 kHz abaixo.
    // Custo: ao ativar, a força não entra na hora — espera a varredura (~9 s).
    if (a0_peek_motor_enable() != 1) motor_link_disable_autostart();
}

extern "C" void ffb_task_start(void) {
    // Stack 4096 B: o pipeline chama sin/cos/pow/tanh (e double) — 512 B estourava e travava.
    osThreadDef(ffbThread, ffb_thread, osPriorityAboveNormal, 0, 4096 / sizeof(StackType_t));
    osThreadCreate(osThread(ffbThread), NULL);
}
