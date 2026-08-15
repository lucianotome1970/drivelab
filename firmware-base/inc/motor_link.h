// firmware-base — ponte ODrive (extern "C"): a ÚNICA costura que toca o core do
// ODrive (isola o class Axis do ODrive do resto). Autor: Luciano Tomé — MIT.
#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif
float motor_link_get_pos_turns(void);         // posição do volante (turns), shadow_count/cpr
void  motor_link_set_input_torque(float nm);  // escreve o torque de FFB (Nm)
float motor_link_get_vel_estimate(void);      // turns/s
float motor_link_get_iq_measured(void);       // A
int   motor_link_motor_is_armed(void);
int      motor_link_axis_state(void);   // AXIS_STATE_* (8 = CLOSED_LOOP)
uint32_t motor_link_axis_error(void);   // AxisError bitfield
uint32_t motor_link_motor_error(void);  // MotorError bitfield
uint32_t motor_link_encoder_error(void);// EncoderError bitfield
uint32_t motor_link_controller_error(void); // ControllerError — o PORQUÊ do axis CONTROLLER_FAILED
uint32_t motor_link_odrv_error(void);       // ODriveError GLOBAL — todo trip de BARRAMENTO está aqui
float    motor_link_get_mech_power(void);   // potência mecânica filtrada (W) — entrada do spinout
float    motor_link_get_elec_power(void);   // potência elétrica filtrada (W) — entrada do spinout
float    motor_link_get_vbus(void);      // tensão do bus (V)
float    motor_link_get_input_torque(void); // torque comandado atual (Nm)
float    motor_link_get_mcu_temp_c(void);  // temperatura do STM32 (sensor interno); -128 = sem leitura
float    motor_link_get_fet_temp_c(void);  // temperatura do estagio de potencia; -128 = sem leitura
float    motor_link_get_max_torque_nm(void); // current_lim x Kt — o teto REAL; 0 = desconhecido
void     motor_link_disable_autostart(void); // desliga auto-arme/calib de boot (boota IDLE, seguro)
void     motor_link_request_idle(void);      // aborta p/ IDLE (motor off)
int32_t  motor_link_enc_cpr(void);
int32_t  motor_link_enc_mode(void);
int32_t  motor_link_enc_use_index(void);
int32_t  motor_link_motor_pole_pairs(void);
int32_t  motor_link_enc_shadow_count(void);
void     motor_link_relax_calibration(void);   // afrouxa cal (cogging do hoverboard)
void     motor_link_request_closed_loop(void); // arme deliberado (cal → closed loop)
int      motor_link_encoder_is_ready(void);    // encoder já referenciado? (0 = precisa calibrar)
// Duas portas para a varredura de offset, vindas de linhas de trabalho diferentes (merge 2026-08-10):
//   _request_encoder_cal      — usada pela trava de bring-up (calibra ao ativar o motor)
//   _request_encoder_calibration — usada pelo "uma calibração por boot" do ffb_task
void     motor_link_request_encoder_cal(void); // só a varredura de offset (motor gira e volta a IDLE)
void     motor_link_request_encoder_calibration(void); // pede a cal de offset (estado 7)
// Tranca a cal recém-concluída (pre_calibrated) e re-inscreve o índice para reancorar a contagem a
// cada volta. Chamar UMA vez, quando is_ready virar true. Ver o comentário longo no .cpp.
void     motor_link_lock_calibration(void);
void     motor_link_enter_dfu(void);            // EnterDfu: reseta e salta pro bootloader da ST
void     motor_link_clear_errors(void);        // limpa erros p/ re-tentar
int32_t  motor_link_startup_flags(void);       // bit0 mcal bit1 ecal bit2 closedloop bit3 idxsearch
int32_t  motor_link_precal_flags(void);        // bit0 motor.precal bit1 enc.precal bit2 use_index bit3 motor.is_calib bit4 enc.is_ready
int32_t  motor_link_motor_R_uohm(void);
int32_t  motor_link_motor_L_nH(void);
void     motor_link_skip_motor_cal(void);      // pula motor cal no boot (sem apito, usa R/L pré-calibrado)
void     motor_link_disable_brake_resistor(void); // desliga a exigência do brake resistor pro arme (bancada)
void     motor_link_newboard_bringup(void);       // calibra do zero o nosso motor na placa nova (pp=15, cpr=4000)
void     motor_link_apply_hw_profile(int board_variant, int bus_nominal_v, int supply_amps); // perfil Placa+Fonte → trips/divider/dc_max
// Deriva rampa/trips do vbus MEDIDO e libera o chopper. Chamar do ffb_task com o motor JÁ ARMADO
// (nunca no boot: lá vbus_voltage ainda é o inicializador 12.0f, e mexer nisso durante a calibração
// a aborta). 1 = fonte presente e dimensionado · 0 = sem fonte, chopper segue mudo.
int      motor_link_autoscale_bus_limits(void);
#ifdef __cplusplus
/// Aplica a configuração de encoder escolhida no app (modelo, tecnologia, resolução). Chamar DEPOIS
/// do bring-up, que é quem fixa os valores de fábrica. Com os settings no padrão o resultado é
/// idêntico ao de hoje. Devolve 1 se a configuração mudou (a calibração passou a ser inválida).
int motor_link_apply_encoder_settings(void);
/// Aplica a configuração de motor escolhida no app (pares de polos, corrente de calibração).
/// Chamar DEPOIS do relax_calibration. Devolve 1 se o número de pares mudou (calibração invalidada).
int motor_link_apply_motor_settings(void);
int motor_link_apply_thermal_settings(void);

/// TESTE DO ENCODER: roda a varredura da calibração alongada para UMA VOLTA, que é o mínimo para
/// medir excentricidade (ver encoder_eccentricity.h). Devolve 0 se não pôde iniciar (motor armado
/// ou sem calibração de motor). ⚠️ Leva ~30 s e o USB pode cair no meio — é teste, não uso normal.
int  motor_link_start_encoder_test(void);
/// Devolve a varredura ao tamanho de produção. Obrigatório depois do teste.
void motor_link_end_encoder_test(void);
}

#endif