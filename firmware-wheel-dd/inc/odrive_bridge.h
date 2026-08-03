// firmware-wheel-dd — ponte ODrive (extern "C"): a ÚNICA costura que toca o core do
// ODrive (isola o class Axis do ODrive do resto). Autor: Luciano Tomé — MIT.
#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
float odrive_bridge_get_pos_turns(void);         // posição do volante (turns), shadow_count/cpr
void  odrive_bridge_set_input_torque(float nm);  // escreve o torque de FFB (Nm)
float odrive_bridge_get_vel_estimate(void);      // turns/s
float odrive_bridge_get_iq_measured(void);       // A
int   odrive_bridge_motor_is_armed(void);
int      odrive_bridge_axis_state(void);   // AXIS_STATE_* (8 = CLOSED_LOOP)
uint32_t odrive_bridge_axis_error(void);   // AxisError bitfield
uint32_t odrive_bridge_motor_error(void);  // MotorError bitfield
uint32_t odrive_bridge_encoder_error(void);// EncoderError bitfield
float    odrive_bridge_get_vbus(void);      // tensão do bus (V)
float    odrive_bridge_get_input_torque(void); // torque comandado atual (Nm)
void     odrive_bridge_disable_autostart(void); // desliga auto-arme/calib de boot (boota IDLE, seguro)
void     odrive_bridge_request_idle(void);      // aborta p/ IDLE (motor off)
int32_t  odrive_bridge_enc_cpr(void);
int32_t  odrive_bridge_enc_mode(void);
int32_t  odrive_bridge_enc_use_index(void);
int32_t  odrive_bridge_motor_pole_pairs(void);
int32_t  odrive_bridge_enc_shadow_count(void);
void     odrive_bridge_relax_calibration(void);   // afrouxa cal (cogging do hoverboard)
void     odrive_bridge_request_closed_loop(void); // arme deliberado (cal → closed loop)
void     odrive_bridge_clear_errors(void);        // limpa erros p/ re-tentar
int32_t  odrive_bridge_startup_flags(void);       // bit0 mcal bit1 ecal bit2 closedloop bit3 idxsearch
int32_t  odrive_bridge_precal_flags(void);        // bit0 motor.precal bit1 enc.precal bit2 use_index bit3 motor.is_calib bit4 enc.is_ready
int32_t  odrive_bridge_motor_R_uohm(void);
int32_t  odrive_bridge_motor_L_nH(void);
void     odrive_bridge_skip_motor_cal(void);      // pula motor cal no boot (sem apito, usa R/L pré-calibrado)
#ifdef __cplusplus
}
#endif
