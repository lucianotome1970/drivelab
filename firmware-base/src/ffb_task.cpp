// firmware-base — ffb_task (STAGE 3b): o laço de FFB de 1 kHz. ESTADO VALIDADO (FFB testado
// no ACC 10+ vezes: força coerente + damper + auto-arme nativo do ODrive, sem runaway).
//
// Cada tick: avança o relógio do modelo, transmite a direção pro host (joystick HID) e, com o
// motor ARMADO, pede o torque do ffb_model (força do jogo + efeitos + mola/damper/endstop) e o
// escreve na ponte. O ODrive faz a FOC/proteção E o auto-arme de boot (startup_closed_loop na NVM).
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#include <stdint.h>
#include <cmsis_os.h>
#include "odrive_bridge.h"
#include "ffb_model.h"
#include "blackbox.h"          // caixa-preta: causa do ultimo reset (lida no boot)
#include "brake_meter.h"          // zerar o medidor do chopper no arme (ver autoscale, abaixo)
extern BrakeMeter g_brake_meter;  // definido em vendor/odrive-fw/MotorControl/low_level.cpp

// Definido em ffb_hid.cpp: joystick (direção pro jogo).
extern "C" int hid_send_joystick(void);      // joystick (direção pro jogo) — PRIORIDADE
extern "C" int a0_service(uint32_t nowMs);   // canal A0 do app (0x16 resposta / 0x21 telemetria) — na sobra
extern "C" bool a0_save_pending(void);       // CMD_SAVE pediu persistir os settings na FFB_NVM?
extern "C" bool a0_commit_save(void);        // empacota + grava na flash (chamar SÓ com motor IDLE)

// Diagnóstico do eixo, lido por SWD (SÓ leitura — não age no motor).
volatile int32_t g_axis_dbg[7] = {0};   // [0]armed [1]state [2]axis_err [3]motor_err [4]enc_err [5]pos*1e3 [6]vel*1e3
// Auto-arme com retry: 1 = após a cal, se ficar em IDLE (mesmo com erro), limpa e pede closed loop.
// A cal do motor às vezes falha (CONTROL_DEADLINE_MISSED, timing); re-tentar fecha o arme. Zerar = seguro.
volatile int32_t g_arm_gate = 1;
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

static void ffb_thread(void*) {
    uint32_t tick = osKernelSysTick();   // base absoluta p/ osDelayUntil (1kHz sem drift)
    uint32_t n = 0;
    for (;;) {
        ffb_model_advance_clock(1);                     // relógio do modelo (1 ms/tick)

        // Diagnóstico do eixo (só leitura por SWD)
        g_axis_dbg[0] = odrive_bridge_motor_is_armed();
        g_axis_dbg[1] = odrive_bridge_axis_state();
        g_axis_dbg[2] = (int32_t)odrive_bridge_axis_error();
        g_axis_dbg[3] = (int32_t)odrive_bridge_motor_error();
        g_axis_dbg[4] = (int32_t)odrive_bridge_encoder_error();
        g_axis_dbg[5] = (int32_t)(odrive_bridge_get_pos_turns() * 1000.0f);
        g_axis_dbg[6] = (int32_t)(odrive_bridge_get_vel_estimate() * 1000.0f);
        g_cfg_dbg[0] = odrive_bridge_startup_flags();
        g_cfg_dbg[1] = odrive_bridge_precal_flags();
        g_cfg_dbg[2] = odrive_bridge_motor_R_uohm();
        g_cfg_dbg[3] = odrive_bridge_motor_L_nH();

        // Save de settings na FFB_NVM (deferido do CMD_SAVE): o erase/program CONGELA a CPU ~centenas de
        // ms (F405 single-bank) → precisa do motor IDLE, senão a ISR do controle perde deadline. Suspende
        // o auto-arme, desarma, grava, e re-permite o arme (o app re-liga a força). ⚠️ A força cai ~1s e
        // o motor re-arma (pode re-calibrar o offset do encoder) — validar o feel na bancada.
        if (a0_save_pending()) {
            g_arm_gate = 0;                              // suspende o auto-arme (senão re-arma na hora)
            if (odrive_bridge_motor_is_armed()) {
                odrive_bridge_request_idle();            // desarma p/ a flash não estourar deadline
            } else {
                odrive_bridge_set_input_torque(0.0f);    // torque zero antes de congelar a CPU
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
        // resistor em si é feito sem janela dentro da função — ver odrive_bridge.cpp.)
        if (!g_bus_autoscaled && g_axis_dbg[1] == 8) {
            g_bus_autoscaled = odrive_bridge_autoscale_bus_limits();
            // Zera o medidor do chopper AQUI: a calibração de offset do boot GIRA o motor, e girar
            // gera regeneração — acionamento legítimo, mas que não é uso. Sem isto a base mostra
            // ~935 acionamentos / 0,8 J assim que liga, e o usuário lê "o chopper está trabalhando
            // parado" (constatado na bancada 2026-08-06). Os contadores passam a medir a SESSÃO:
            // do arme em diante. Zero depois de dirigir continua sendo diagnóstico de resistor
            // desconectado ou rampa mal dimensionada, que é a razão de a feature existir.
            brake_meter_init(&g_brake_meter);
        }

        // Auto-arme com retry ESPAÇADO + timeout de segurança.
        //
        // Era: 1 tentativa por ENTRADA em IDLE (`!s_was_idle`), teto 15 → uma vez preso em IDLE
        // nunca mais tentava, e 15 falhas seguidas desistiam PARA SEMPRE (FFB morto, sem aviso).
        // Foi o que travou o teste do brake chopper em 2026-08-04: a sobretensão desarma o motor E o
        // brake resistor juntos (ODrive::disarm_with_error → safety_critical_disarm_brake_resistor),
        // aí (enable && !armed) derruba toda tentativa de arme com BRAKE_RESISTOR_DISARMED até
        // esgotar o teto. Agora clear_errors() re-arma o brake (ver odrive_bridge.cpp), então o
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
            static uint32_t s_next_try     = 0;   // tick da próxima tentativa (backoff)
            static uint32_t s_armed_ticks  = 0;   // há quanto tempo está armado
            const int st = g_axis_dbg[1];
            const bool in_cal = (st == 3 || st == 4 || st == 6 || st == 7);
            if (in_cal) {
                if (++s_cal_ticks > 12000) odrive_bridge_request_idle();
            } else {
                s_cal_ticks = 0;
                if (st == 8) {
                    if (s_armed_ticks <= 1000 && ++s_armed_ticks > 1000) s_arm_attempts = 0;
                } else if (st == 1 && g_arm_gate == 1) {
                    s_armed_ticks = 0;
                    // (int32_t) na diferença = seguro no wrap do contador de 1 kHz
                    if ((int32_t)(n - s_next_try) >= 0 && s_arm_attempts < 300) {
                        // ANTES do clear: fotografa a falha, senão ela se perde (ver g_fail_dbg).
                        if (g_fail_dbg[0] == 0) {
                            g_fail_dbg[1] = (int32_t)odrive_bridge_controller_error();
                            g_fail_dbg[2] = g_axis_dbg[2];   // axis_err
                            g_fail_dbg[3] = g_axis_dbg[3];   // motor_err
                            g_fail_dbg[4] = g_axis_dbg[4];   // enc_err
                            g_fail_dbg[5] = (int32_t)(odrive_bridge_get_mech_power() * 1000.0f);
                            g_fail_dbg[6] = (int32_t)(odrive_bridge_get_elec_power() * 1000.0f);
                            g_fail_dbg[7] = (int32_t)(odrive_bridge_get_vbus() * 1000.0f);
                        }
                        g_fail_dbg[0]++;
                        odrive_bridge_clear_errors();      // limpa erros + RE-ARMA o brake resistor
                        odrive_bridge_request_closed_loop();
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
        n++;

        if (g_axis_dbg[0]) {   // só age com o motor ARMADO (CLOSED_LOOP)
            const float pos = odrive_bridge_get_pos_turns();     // turns (encoder)
            const float vel = odrive_bridge_get_vel_estimate();  // turns/s
            odrive_bridge_set_input_torque(ffb_model_compute_torque(pos, vel));
        } else {
            odrive_bridge_set_input_torque(0.0f);
            ffb_model_reset_clipping();   // desarmado não clipa — não deixar o último valor congelado na tela
        }
        osDelayUntil(&tick, 1);   // 1 kHz absoluto (sem drift)
    }
}

extern "C" void ffb_init_storage_early(void) {
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
    odrive_bridge_newboard_bringup();
}

extern "C" void ffb_task_init(void) {
    // Stack 4096 B: o pipeline chama sin/cos/pow/tanh (e double) — 512 B estourava e travava.
    osThreadDef(ffbThread, ffb_thread, osPriorityAboveNormal, 0, 4096 / sizeof(StackType_t));
    osThreadCreate(osThread(ffbThread), NULL);
}
