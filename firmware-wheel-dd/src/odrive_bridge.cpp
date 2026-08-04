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
// Limpa erros (axis/motor/encoder) p/ permitir nova tentativa de arme.
extern "C" void odrive_bridge_clear_errors(void) {
    axes[0].error_ = Axis::ERROR_NONE;
    axes[0].motor_.error_ = Motor::ERROR_NONE;
    axes[0].encoder_.error_ = Encoder::ERROR_NONE;
    axes[0].controller_.error_ = Controller::ERROR_NONE;
}
