// firmware-base — stubs MIT de TODAS as funcoes ffb_* que o ascii_protocol patchado usa.
// Diag/params FFB do protocolo ASCII que nao usamos — retornam 0/no-op. Os nossos sao outros.
#include <stdint.h>
extern "C" {
uint32_t ffb_diag_hidout_total(void) { return 0; }
uint32_t ffb_diag_hidout_ctrl(void) { return 0; }
uint32_t ffb_diag_hidout_neweff(void) { return 0; }
uint32_t ffb_diag_hidout_seteff(void) { return 0; }
uint32_t ffb_diag_hidout_cond(void) { return 0; }
uint32_t ffb_diag_hidout_const(void) { return 0; }
uint32_t ffb_diag_hidout_period(void) { return 0; }
uint32_t ffb_diag_hidout_efop(void) { return 0; }
uint32_t ffb_diag_hidout_gain(void) { return 0; }
uint32_t ffb_diag_hidout_other(void) { return 0; }
uint32_t ffb_diag_hidget(void) { return 0; }
uint32_t ffb_diag_set_eff_torque(void) { return 0; }
int32_t ffb_diag_last_torque(void) { return 0; }
int ffb_diag_active_effects(void) { return 0; }
float ffb_diag_pending_torque(void) { return 0; }
int ffb_diag_ffb_active_flag(void) { return 0; }
int ffb_diag_eff_index(void) { return 0; }
int ffb_diag_eff_state(void) { return 0; }
int ffb_diag_eff_type(void) { return 0; }
int32_t ffb_diag_eff_magnitude(void) { return 0; }
float ffb_diag_eff_axmag0(void) { return 0; }
int ffb_diag_eff_gain(void) { return 0; }
int ffb_diag_global_gain(void) { return 0; }
int ffb_diag_eff_index_n(int n) { return 0; }
int ffb_diag_eff_state_n(int n) { return 0; }
int ffb_diag_eff_type_n(int n) { return 0; }
int32_t ffb_diag_eff_magnitude_n(int n) { return 0; }
float ffb_diag_eff_axmag0_n(int n) { return 0; }
int ffb_diag_eff_gain_n(int n) { return 0; }
int ffb_diag_slot_state(int slot) { return 0; }
int ffb_diag_slot_type(int slot) { return 0; }
int32_t ffb_diag_slot_magnitude(int slot) { return 0; }
int ffb_diag_total_slots(void) { return 0; }
float ffb_diag_ibus_max(void) { return 0; }
float ffb_diag_ibus_min(void) { return 0; }
float ffb_diag_motor_ibus_max(void) { return 0; }
float ffb_diag_motor_ibus_min(void) { return 0; }
float ffb_diag_vbus_max(void) { return 0; }
float ffb_diag_vbus_min(void) { return 0; }
void ffb_diag_reset_ibus_peaks(void) { }
float ffb_get_axis_range(void) { return 0; }
float ffb_get_axis_maxtq(void) { return 0; }
float ffb_get_axis_fxratio(void) { return 0; }
void ffb_set_axis_range(float v) { }
void ffb_set_axis_maxtq(float v) { }
void ffb_set_axis_fxratio(float v) { }
}
