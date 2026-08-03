// firmware-wheel-dd — ffb_task (STAGE 3b): o laço de FFB de 1 kHz. ESTADO VALIDADO (FFB testado
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

// Definido em ffb_hid.cpp: joystick (direção pro jogo).
extern "C" int hid_send_joystick(void);      // joystick (direção pro jogo) — PRIORIDADE
extern "C" int a0_service(uint32_t nowMs);   // canal A0 do app (0x16 resposta / 0x21 telemetria) — na sobra

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

        // Auto-arme com retry + timeout de segurança:
        //  IDLE (com/sem erro) + g_arm_gate → limpa erros e pede closed loop (1 pedido por entrada em
        //  IDLE; teto 15 tentativas). Preso numa cal (3/4/6/7) >12s → aborta (motor off). Armado → reseta.
        {
            static uint32_t s_cal_ticks = 0;
            static int  s_arm_attempts = 0;
            static bool s_was_idle = false;
            const int st = g_axis_dbg[1];
            const bool in_cal = (st == 3 || st == 4 || st == 6 || st == 7);
            if (in_cal) {
                if (++s_cal_ticks > 12000) odrive_bridge_request_idle();
            } else {
                s_cal_ticks = 0;
                if (st == 8) s_arm_attempts = 0;
                else if (st == 1 && !s_was_idle && g_arm_gate == 1 && s_arm_attempts < 15) {
                    odrive_bridge_clear_errors();
                    odrive_bridge_request_closed_loop();
                    s_arm_attempts++;
                }
            }
            s_was_idle = (st == 1);
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
        }
        osDelayUntil(&tick, 1);   // 1 kHz absoluto (sem drift)
    }
}

extern "C" void ffb_init_storage_early(void) {
    // SEM APITO + arme confiável: pula a medição do motor no boot (usa o R/L já guardado,
    // motor.pre_calibrated=TRUE). Roda ANTES do eixo iniciar (main.cpp:544 < :604). Mantém a
    // cal de offset do encoder (movimento) + o auto-arme nativo (startup_closed_loop na NVM).
    odrive_bridge_skip_motor_cal();
}

extern "C" void ffb_task_init(void) {
    // Stack 4096 B: o pipeline chama sin/cos/pow/tanh (e double) — 512 B estourava e travava.
    osThreadDef(ffbThread, ffb_thread, osPriorityAboveNormal, 0, 4096 / sizeof(StackType_t));
    osThreadCreate(osThread(ffbThread), NULL);
}
