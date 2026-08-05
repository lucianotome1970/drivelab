// firmware-wheel-dd — ponte ODrive. ÚNICO arquivo que inclui odrive_main.h (isola o
// class Axis do ODrive). API extern "C" plana pro nosso ffb_task. Autor: Luciano Tomé — MIT.
#include "odrive_bridge.h"
#include "odrive_main.h"

extern "C" float odrive_bridge_get_pos_turns(void) {
    int32_t cnt = axes[0].encoder_.shadow_count_;      // int32 acumulado (não wrappa) — ESTADO VALIDADO
    int32_t cpr = axes[0].encoder_.config_.cpr;
    return cpr ? (float)cnt / (float)cpr : 0.0f;
}
extern "C" void odrive_bridge_set_input_torque(float nm) {
    // Efetivo só em CLOSED_LOOP + control_mode=TORQUE + input_mode=PASSTHROUGH.
    // Fora disso o Controller::update() ignora → seguro chamar todo tick.
    axes[0].controller_.input_torque_ = nm;
}
extern "C" float odrive_bridge_get_vel_estimate(void) {
    return axes[0].encoder_.vel_estimate_.any().value_or(0.0f);
}
extern "C" float odrive_bridge_get_iq_measured(void) {
    return axes[0].motor_.current_control_.Iq_measured_;
}
extern "C" int odrive_bridge_motor_is_armed(void) {
    return axes[0].motor_.is_armed_ ? 1 : 0;
}
extern "C" int      odrive_bridge_axis_state(void)  { return (int)axes[0].current_state_; }
extern "C" uint32_t odrive_bridge_axis_error(void)  { return (uint32_t)axes[0].error_; }
extern "C" uint32_t odrive_bridge_motor_error(void) { return (uint32_t)axes[0].motor_.error_; }
extern "C" uint32_t odrive_bridge_encoder_error(void) { return (uint32_t)axes[0].encoder_.error_; }
extern "C" float odrive_bridge_get_vbus(void) { return vbus_voltage; }
extern "C" float odrive_bridge_get_input_torque(void) { return axes[0].controller_.input_torque_; }

// SEGURANÇA: desabilita todo o auto-arme/calibração de boot. A calibração de offset do encoder
// no boot é INSTÁVEL nesta placa (CPR_MISMATCH intermitente) e, quando trava, GIRA o motor sem
// parar. Chamado em ffb_init_storage_early (antes dos threads do eixo) → boota em IDLE (motor off).
// Arme passa a ser DELIBERADO (não usa o startup do ODrive). Runtime override (não mexe na NVM).
extern "C" void odrive_bridge_disable_autostart(void) {
    axes[0].config_.startup_closed_loop_control = false;
    axes[0].config_.startup_encoder_offset_calibration = false;
    axes[0].config_.startup_motor_calibration = false;
    axes[0].config_.startup_encoder_index_search = false;
}
// Aborta qualquer estado != IDLE (rede de segurança): pede IDLE → o motor desliga.
extern "C" void odrive_bridge_request_idle(void) { axes[0].requested_state_ = Axis::AXIS_STATE_IDLE; }

// Leitores da config do encoder/motor (diagnóstico do CPR_MISMATCH).
extern "C" int32_t odrive_bridge_enc_cpr(void)        { return (int32_t)axes[0].encoder_.config_.cpr; }
extern "C" int32_t odrive_bridge_enc_mode(void)       { return (int32_t)axes[0].encoder_.config_.mode; }
extern "C" int32_t odrive_bridge_enc_use_index(void)  { return axes[0].encoder_.config_.use_index ? 1 : 0; }
extern "C" int32_t odrive_bridge_motor_pole_pairs(void){ return (int32_t)axes[0].motor_.config_.pole_pairs; }
extern "C" int32_t odrive_bridge_enc_shadow_count(void){ return axes[0].encoder_.shadow_count_; }

// Config real (NVM) — diagnóstico do "não calibra/não arma/apito".
extern "C" int32_t odrive_bridge_startup_flags(void) {
    return (axes[0].config_.startup_motor_calibration          ? 1 : 0)
         | (axes[0].config_.startup_encoder_offset_calibration ? 2 : 0)
         | (axes[0].config_.startup_closed_loop_control        ? 4 : 0)
         | (axes[0].config_.startup_encoder_index_search       ? 8 : 0);
}
extern "C" int32_t odrive_bridge_precal_flags(void) {
    return (axes[0].motor_.config_.pre_calibrated  ? 1 : 0)
         | (axes[0].encoder_.config_.pre_calibrated? 2 : 0)
         | (axes[0].encoder_.config_.use_index     ? 4 : 0)
         | (axes[0].motor_.is_calibrated_          ? 8 : 0)
         | (axes[0].encoder_.is_ready_             ? 16 : 0);
}
extern "C" int32_t odrive_bridge_motor_R_uohm(void) { return (int32_t)(axes[0].motor_.config_.phase_resistance * 1000000.0f); }
extern "C" int32_t odrive_bridge_motor_L_nH(void)   { return (int32_t)(axes[0].motor_.config_.phase_inductance * 1e9f); }

// SEM APITO + arme confiável: pula a MEDIÇÃO do motor no boot (o apito) usando o R/L já guardado
// (motor.pre_calibrated=TRUE, is_calibrated=TRUE). Também pula a etapa que falhava (MOTOR_FAILED).
// Mantém a cal de offset do encoder (movimento, não apito) + o auto-arme. Runtime override (não NVM).
extern "C" void odrive_bridge_skip_motor_cal(void) {
    axes[0].config_.startup_motor_calibration = false;
}

// Desliga a EXIGÊNCIA do brake resistor pro arme (o ODrive desarma se enable&&!armed). O resistor da
// bancada nunca conduziu → brake_resistor_armed=0 → o motor desarmava (BRAKE_RESISTOR_DISARMED). A ~19,6V
// a regen do volante é pequena (caps + proteção de sobretensão cobrem). ⚠️ Reabilitar quando o brake
// resistor for validado / em bus alto (56V). Runtime override (não NVM). Ver 08e1b22 / drivelab-brake-chopper.
extern "C" void odrive_bridge_disable_brake_resistor(void) {
    odrv.config_.enable_brake_resistor = false;
}

// Escala do vbus (divider ADC) — definida em ffb_task.cpp, aplicada no low_level (runtime, não compile-time).
extern volatile float g_vbus_voltage_scale;

// PERFIL DE HARDWARE (Placa + Fonte) → deriva os limites de segurança. É a MESMA lógica que o app espelha
// pra mostrar pro criador. Chamado do a0_apply_settings quando o app manda o perfil (e no init com defaults).
//   board_variant: 0 = placa 56V (divider 19, OVP 55V) · 1 = placa 24V (divider 11, OVP 28V — caps ~30V!)
//   bus_nominal_v: tensão nominal da fonte [V] (reservado p/ display; UVP fixo em 8V "prevents brown-outs")
//   supply_amps:   corrente máx da fonte [A] → dc_max_positive_current (trip de proteção da fonte). 0 = não aplica.
extern "C" void odrive_bridge_apply_hw_profile(int board_variant, int bus_nominal_v, int supply_amps) {
    if (board_variant == 1) {                                  // PLACA 24V
        g_vbus_voltage_scale = 3.3f * 11.0f / 4096.0f;         // divider 11
        odrv.config_.dc_bus_overvoltage_trip_level = 28.0f;    // caps ~30V → teto BAIXO
    } else {                                                   // PLACA 56V (default)
        g_vbus_voltage_scale = 3.3f * 19.0f / 4096.0f;         // divider 19
        odrv.config_.dc_bus_overvoltage_trip_level = 55.0f;    // folga p/ regen (caps ~63V)
    }
    odrv.config_.dc_bus_undervoltage_trip_level = 8.0f;        // piso anti brown-out
    if (supply_amps > 0)
        odrv.config_.dc_max_positive_current = (float)supply_amps;  // proteção da fonte (trip se puxar demais)
    (void)bus_nominal_v;                                       // reservado (display do nominal; UVP fixo por ora)
}

// BRING-UP de PLACA NOVA: calibra o NOSSO motor do zero (a NVM de fábrica é de outro motor). Seta a
// geometria (pole_pairs=15 do hoverboard, cpr=4000 do E6B2 1000 PPR ×4, incremental), corrente de cal
// SEGURA (10A, não os 30 do perfil) e limite modesto (falha-seguro). Faz cal completa (mede R/L → "apito")
// + offset do encoder + arma no fim. Brake desligado. Roda 1x na placa nova pra testar o DRV limpo.
extern "C" void odrive_bridge_newboard_bringup(void) {
    // MOTOR — geometria + R/L do NOSSO hoverboard (medidos na placa antiga: 0,20Ω/0,35mH; batem com o
    // config hoverboard do Odrive-Wheel 0,174Ω/0,349mH). PRÉ-CALIBRADO → is_calibrated_ SEM medir R/L.
    // ⚠️ CHAVE: pular a medição de indutância é o que EVITA o DRV_FAULT (L baixa → ΔI=V·Δt/L explode →
    // OCP do DRV8301). Confirmado pelo core do ODrive (measure_phase_inductance). NÃO por
    // startup_motor_calibration=true (foi o meu erro anterior — rodava a medição perigosa).
    axes[0].motor_.config_.motor_type               = Motor::MOTOR_TYPE_HIGH_CURRENT;
    axes[0].motor_.config_.pole_pairs               = 15;
    axes[0].motor_.config_.torque_constant          = 0.55f;      // hoverboard (Odrive-Wheel)
    axes[0].motor_.config_.phase_resistance         = 0.20f;      // medido na placa antiga
    axes[0].motor_.config_.phase_inductance         = 0.00035f;   // medido na placa antiga (L baixa)
    axes[0].motor_.config_.pre_calibrated           = true;       // aplica is_calibrated_ no apply_config
    axes[0].motor_.config_.calibration_current      = 3.0f;       // VALIDADO na bancada: 3A calibra + arma + endstop dos 2 lados. 8A tripava o DRV (o churn "UNKNOWN_PHASE" era o SWD halt, não a corrente).
    axes[0].motor_.config_.requested_current_range  = 60.0f;      // headroom de saturação (não abaixar!)
    axes[0].motor_.config_.current_control_bandwidth= 200.0f;     // L baixa → NÃO 1000 (default nosso já 200)
    axes[0].motor_.config_.current_lim              = 25.0f;
    axes[0].motor_.config_.current_lim_margin       = 8.0f;
    // ENCODER — incremental SEM Z → offset cal OBRIGATÓRIA a cada boot (o ODrive recusa is_ready pra
    // incremental sem índice; pre_calibrated não vale). Consistência real só ligando o fio Z depois.
    axes[0].encoder_.config_.mode                   = Encoder::MODE_INCREMENTAL;
    axes[0].encoder_.config_.cpr                    = 4000;       // E6B2 1000 PPR ×4
    axes[0].encoder_.config_.bandwidth              = 200.0f;
    axes[0].encoder_.config_.calib_range            = 0.02f;
    axes[0].encoder_.config_.use_index              = false;
    axes[0].encoder_.config_.pre_calibrated         = false;
    // STARTUP — PULA a medição de R/L (pre_calibrated), roda SÓ a offset do encoder, e arma.
    axes[0].config_.startup_motor_calibration            = false; // ← FIX: não mede R/L (sem DRV_FAULT)
    axes[0].config_.startup_encoder_offset_calibration   = true;  // obrigatório: incremental sem Z
    axes[0].config_.startup_closed_loop_control          = true;  // auto-arma no fim
    // Sim racing: sem clamp de velocidade cortando torque (girar na mão sem OVERSPEED)
    axes[0].controller_.config_.enable_vel_limit         = false;
    // BRAKE RESISTOR (chopper) — REPLICA o config funcional do Odrive-Wheel/ODESC: dissipa a regen que
    // estourava dc_bus_overvoltage_trip_level (a queda de FFB na chicane). Antes ficava DESLIGADO (contorno
    // do não-arme); agora o clear_errors (delegado a odrv.clear_errors, fix 046c421) RE-ARMA o brake no
    // auto-arme → satisfaz enable&&armed → o motor arma junto. Setado no boot (não depende de NVM stale).
    // ⚠️ Valores por-PLACA (56V aqui) → vira board profile na Fase 2. brake_resistance idealmente é setting
    // da aba Hardware (feat/brake-resistance); aqui fixo em 2.0 = nosso resistor físico (Odrive-Wheel usa 12Ω).
    odrv.config_.enable_brake_resistor           = true;   // LIGADO (arma via clear_errors → dissipa regen)
    odrv.config_.brake_resistance                = 2.0f;   // nosso resistor físico de 2Ω
    odrv.config_.dc_bus_overvoltage_trip_level   = 55.0f;  // teto seguro placa 56V (era ~24,79 na NVM → tripava)
    odrv.config_.dc_bus_undervoltage_trip_level  = 8.0f;   // "prevents brown-outs" (Odrive-Wheel); garante no boot
}

// Afrouxa a calibração p/ vencer o cogging do hoverboard (raiz do CPR_MISMATCH intermitente):
// mais tolerância no check de CPR + mais corrente no scan (motion suave → counts corretos).
extern "C" void odrive_bridge_relax_calibration(void) {
    axes[0].encoder_.config_.calib_range = 0.05f;         // 2% → 5% (tolera desvio do cogging)
    axes[0].motor_.config_.calibration_current = 5.0f;    // 3A → 5A (vence o cogging no scan)
}
// Arme deliberado: pede CLOSED_LOOP (roda cal → closed loop).
extern "C" void odrive_bridge_request_closed_loop(void) {
    axes[0].requested_state_ = Axis::AXIS_STATE_CLOSED_LOOP_CONTROL;
}
// Limpa erros p/ permitir nova tentativa de arme. Delega ao clear_errors() do ODrive
// (MotorControl/main.cpp) em vez de zerar os campos na mão, porque ele faz DUAS coisas
// que a versão manual não fazia e que travavam a recuperação:
//   1) zera o erro GLOBAL (odrv.error_) — senão o BUS_OVER_V fica grudado pra sempre;
//   2) RE-ARMA o brake resistor se enable_brake_resistor estiver ligado.
// Sem (2) o motor NUNCA voltava com o chopper ligado: a sobretensão da regen chama
// ODrive::disarm_with_error(), que desarma o brake junto (low_level.cpp:97); a partir daí
// (enable && !armed) faz apply_pwm_timings() derrubar TODA tentativa de arme com
// BRAKE_RESISTOR_DISARMED, até o auto-arme esgotar as 15 tentativas e desistir de vez.
// Diagnosticado na bancada 2026-08-04 (perda TOTAL de FFB, 82 amostras presas em IDLE).
extern "C" void odrive_bridge_clear_errors(void) {
    odrv.clear_errors();
}
