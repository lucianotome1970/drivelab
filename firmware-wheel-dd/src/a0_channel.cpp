// firmware-wheel-dd — canal A0 (protocolo do DriveLab Studio). Replica a ABORDAGEM do firmware-base
// (lib/base_usb/a0_channel.cpp) adaptada ao TinyUSB CRU + ao nosso ffb_model:
//   - a0_handle_out(): despacha OUT reports por buf[0] (0x14 SETWRITE / 0x15 SETREAD / 0x22 CMD /
//     0x10 DIRECT). SÓ guarda valores/seta flags — trabalho pesado fica no laço, não no callback USB.
//   - a0_service(): no laço, envia com PRIORIDADE a resposta deferida 0x16 (SETVALUE) e, na sobra,
//     a telemetria periódica 0x21 (DeviceState). Cada envio é gated por tud_hid_ready() (um por janela).
//   - DeviceState (0x21): layout casado byte-a-byte com DriveLab.Core/Protocol/BaseState.cs
//     (Position ±10000, AngleDeciDeg em décimos de grau — é o que gira o volante na tela do app).
//   - Settings: store em RAM (45 campos, com defaults do schema) + read/write; os principais aplicam
//     no ffb_model (força/direção). Center (ResetCenter) captura a posição atual como zero.
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#include <stdint.h>
#include <string.h>
#include "tusb.h"
#include "odrive_bridge.h"
#include "ffb_model.h"

// Report IDs do canal A0 (ver a0_hid_descriptor.h; definidos localmente p/ não re-incluir o array).
#define A0_RID_STATE    0x21   // IN  DeviceState (telemetria/ângulo)
#define A0_RID_SETVALUE 0x16   // IN  resposta de leitura de setting
#define A0_RID_CMD      0x22   // OUT comando [cmdId, arg]
#define A0_RID_DIRECT   0x10   // OUT forças diretas do app
#define A0_RID_SETWRITE 0x14   // OUT grava setting [fieldId, idx, type, valor]
#define A0_RID_SETREAD  0x15   // OUT pede leitura [fieldId, idx]
#define A0_PAYLOAD      63     // ReportConstants.ReportSize

// SettingType (DriveLab.Core.Settings.SettingType)
enum { T_U8 = 0, T_I8 = 1, T_U16 = 2, T_I16 = 3, T_FLOAT = 4 };

// BaseCommand (DriveLab.Core.Settings.BaseCommand)
enum { CMD_REBOOT = 1, CMD_SAVE = 2, CMD_RESET_CENTER = 3, CMD_DFU = 4, CMD_CALIBRATE = 5,
       CMD_SET_FORCE_ENABLED = 6, CMD_CAL_COGGING = 7, CMD_BRAKE_BENCH = 8, CMD_BRAKE_AUTO = 9 };

#define A0_NUM_SETTINGS 45

// Tipo de cada FieldId (BaseSettingsSchema). Índice = BaseSettingId.
static const uint8_t s_type[A0_NUM_SETTINGS] = {
    /*0 motion_range*/T_U16, /*1 soft_stop_range*/T_U8, /*2 soft_stop_strength*/T_U8,
    /*3 total_strength*/T_U8, /*4 spring_strength*/T_U8, /*5 damper_strength*/T_U8,
    /*6 static_damping*/T_U8, /*7 max_torque_limit*/T_U8, /*8 force_direction*/T_I8,
    /*9 encoder_direction*/T_I8, /*10 encoder_cpr*/T_U16, /*11 pole_pairs*/T_U8,
    /*12 current_p*/T_FLOAT, /*13 current_i*/T_FLOAT, /*14 calibration_current*/T_U8,
    /*15 position_smoothing*/T_U8, /*16 power_limit*/T_U8, /*17 braking_limit*/T_U8,
    /*18 encoder_type*/T_U8, /*19 reconstruction_steps*/T_U8, /*20 reconstruction_lpf*/T_U8,
    /*21 output_filter_hz*/T_U16, /*22 osc_guard*/T_U8, /*23 endstop_damping*/T_U8,
    /*24 linearity*/T_U8, /*25 cogging_enable*/T_U8, /*26 slew_rate*/T_U8, /*27 bus_nominal_v*/T_U8,
    /*28..32 ffb_curve*/T_U8, T_U8, T_U8, T_U8, T_U8, /*33 board_variant*/T_U8,
    /*34 torque_constant*/T_FLOAT, /*35 thermal_continuous*/T_U8, /*36 thermal_peak_s*/T_U8,
    /*37 fet_temp_limit*/T_U8, /*38 motor_temp_limit*/T_U8, /*39..42 gains*/T_U8, T_U8, T_U8, T_U8,
    /*43 soft_power*/T_U8, /*44 power_button*/T_U8,
};

// Valor de cada campo: inteiro em s_ival (u8/i8/u16/i16) OU float em s_fval (T_FLOAT).
static int32_t s_ival[A0_NUM_SETTINGS];
static float   s_fval[A0_NUM_SETTINGS];

static void a0_load_defaults(void) {
    static const int32_t def[A0_NUM_SETTINGS] = {
        900, 5, 80, 100, 0, 10, 5, 80, 1, 1, 4000, 15, 0, 0, 30, 0, 100, 100, 0, 0, 0, 0, 0, 0,
        100, 0, 0, 56, 0, 25, 50, 75, 100, 1, 0, 100, 0, 85, 100, 100, 100, 100, 100, 0, 0
    };
    for (int i = 0; i < A0_NUM_SETTINGS; ++i) { s_ival[i] = def[i]; s_fval[i] = 0.0f; }
    s_fval[12] = 0.05f;   // current_p
    s_fval[13] = 10.0f;   // current_i
    s_fval[34] = 0.0f;    // torque_constant
}

// --- estado de envio / center ---
static float   s_center_turns = 0.0f;     // offset de centro (ResetCenter)
static bool    s_force_enabled = true;
static uint8_t s_pending_read = 0xFF;     // fieldId pendente de resposta 0x16 (0xFF = nenhum)
static uint32_t s_last_state_ms = 0;
static bool    s_inited = false;

static inline uint16_t rd_u16(const uint8_t* p) { return (uint16_t)p[0] | ((uint16_t)p[1] << 8); }
static inline void put_i16(uint8_t* p, int16_t v)  { p[0] = (uint8_t)(v & 0xFF); p[1] = (uint8_t)((v >> 8) & 0xFF); }
static inline void put_u16(uint8_t* p, uint16_t v) { p[0] = (uint8_t)(v & 0xFF); p[1] = (uint8_t)((v >> 8) & 0xFF); }
static inline int16_t clip_i16(float v) { return v > 32767.0f ? 32767 : (v < -32767.0f ? -32767 : (int16_t)v); }

// Aplica os settings que afetam o FFB no ffb_model (os demais ficam guardados p/ read-back).
static void a0_apply_settings(void) {
    ffb_model_set_config(
        (float)s_ival[3],    // total_strength %  (força)
        (float)s_ival[7],    // max_torque_limit %
        (int)s_ival[8],      // force_direction (-1/+1)
        (float)s_ival[4],    // spring_strength %
        (float)s_ival[5],    // damper_strength %
        (int)s_ival[0],      // motion_range (graus, DOR)
        (int)s_ival[39], (int)s_ival[40], (int)s_ival[41], (int)s_ival[42], // gains do jogo
        (int)s_ival[24]   // P0: linearity (curva de resposta)
    );
    // P0: ajustes avançados (struct que cresce por setting ligado)
    FfbTuning tune = { (int)s_ival[6], (int)s_ival[23], (int)s_ival[26] };  // static_damping, endstop_damping, slew_rate
    ffb_model_apply_tuning(&tune);
    // PERFIL DE HARDWARE (Placa + Fonte) → deriva divider + trips (mesma lógica espelhada no app).
    //   board_variant(33): 0=placa 24V · 1=placa 56V (default)   ·   bus_nominal_v(27): tensão da fonte [V]
    //   amperagem: 0 (sem campo dedicado ainda; power_limit(16) é "%", não A) → dc_max = follow-up.
    odrive_bridge_apply_hw_profile((int)s_ival[33], (int)s_ival[27], 0);
}

static void a0_init(void) {
    a0_load_defaults();
    a0_apply_settings();
    s_inited = true;
}

// ---------------------------------------------------------------------------
// OUT reports (host→device) — chamado do tud_hid_set_report_cb. Só guarda/flag.
// ---------------------------------------------------------------------------
extern "C" void a0_handle_out(const uint8_t* buf, uint16_t len) {
    if (!s_inited) a0_init();
    if (buf == nullptr || len < 1) return;

    switch (buf[0]) {
        case A0_RID_SETWRITE: {            // wire: [0x14, FieldId, Index, Type, valor LE]
            if (len < 5) return;
            const uint8_t id = buf[1];
            if (id >= A0_NUM_SETTINGS) return;
            const uint8_t type = buf[3];   // buf[2]=Index (=0, ignorado)
            switch (type) {
                case T_U8:  s_ival[id] = buf[4]; break;
                case T_I8:  s_ival[id] = (int8_t)buf[4]; break;
                case T_U16: if (len >= 6) s_ival[id] = rd_u16(&buf[4]); break;
                case T_I16: if (len >= 6) s_ival[id] = (int16_t)rd_u16(&buf[4]); break;
                case T_FLOAT: if (len >= 8) memcpy(&s_fval[id], &buf[4], 4); break;
                default: break;
            }
            a0_apply_settings();
            break;
        }
        case A0_RID_SETREAD:               // wire: [0x15, FieldId, Index] → responde 0x16 no a0_service
            if (len >= 2 && buf[1] < A0_NUM_SETTINGS) s_pending_read = buf[1];
            break;
        case A0_RID_CMD: {                 // wire: [0x22, CommandId, Arg]
            if (len < 2) return;
            const uint8_t cmd = buf[1];
            const uint8_t arg = (len >= 3) ? buf[2] : 0;
            switch (cmd) {
                case CMD_RESET_CENTER:  s_center_turns = odrive_bridge_get_pos_turns(); break;  // zero = posição atual
                case CMD_SET_FORCE_ENABLED: s_force_enabled = (arg != 0); break;
                // CMD_SAVE (flash), CMD_REBOOT, CMD_DFU, CMD_CALIBRATE: TODO (trabalho no loop, não aqui)
                default: break;
            }
            break;
        }
        case A0_RID_DIRECT:                // [spring i16, constant i16, periodic i16, damper i16, drop u8, telem i16]
            if (len >= 12) {
                int16_t telem = (int16_t)rd_u16(&buf[10]);
                ffb_model_set_telemetry_force((float)telem);   // força aditiva de telemetria do app
            }
            break;
        default: break;
    }
}

// Posição do volante já com o offset de centro (ResetCenter).
static float a0_centered_turns(void) { return odrive_bridge_get_pos_turns() - s_center_turns; }

// ---------------------------------------------------------------------------
// Monta o DeviceState (0x21) — layout de BaseState.cs.
// ---------------------------------------------------------------------------
static void a0_build_state(uint8_t* p) {
    memset(p, 0, A0_PAYLOAD);
    p[0] = 0; p[1] = 0; p[2] = 4; p[3] = 0;                 // Firmware 0.4.0 (dev)

    uint8_t flags = 0;
    const bool armed = odrive_bridge_motor_is_armed();
    if (armed && s_force_enabled) flags |= 0x01;            // ForceEnabled
    if (armed)                    flags |= 0x02;            // Calibrated (armado = calibrado)
    if (odrive_bridge_axis_error() != 0) flags |= 0x04;     // Error
    p[4] = flags;                                           // NÃO setar bit3 (UsingSimulator)

    const float turns = a0_centered_turns();
    float norm = turns / 1.5f;                              // ±1.5 turns → ±100%
    if (norm > 1.0f) norm = 1.0f; else if (norm < -1.0f) norm = -1.0f;
    put_i16(&p[5], (int16_t)(norm * 10000.0f));            // Position ±10000
    put_i16(&p[7], clip_i16(turns * 3600.0f));            // AngleDeciDeg (décimos de grau) ← gira o volante
    put_i16(&p[9], clip_i16(odrive_bridge_get_input_torque() / 5.0f * 10000.0f));  // Torque ±10000 (teto 5Nm)
    put_i16(&p[11], clip_i16(odrive_bridge_get_iq_measured() * 1000.0f));          // MotorCurrentMa
    p[13] = (uint8_t)(int8_t)(-128);                       // FetTempC (sem sensor)
    p[14] = (uint8_t)(odrive_bridge_axis_error() & 0xFF);  // ErrorCode
    put_u16(&p[15], (uint16_t)(odrive_bridge_get_vbus() * 1000.0f));               // BusVoltageMv
    p[17] = (uint8_t)(int8_t)(-128);                       // MotorTempC (sem sensor)
    p[18] = 0;                                             // McuTempC (TODO)
    p[19] = 0;                                             // Clipping (TODO)
}

// ---------------------------------------------------------------------------
// Envio no laço (a0_service): PRIORIDADE resposta 0x16 > telemetria 0x21. Gated por ready().
// nowMs: relógio em ms. Retorna true se enviou algo (o chamador pode dar prioridade ao joystick).
// ---------------------------------------------------------------------------
extern "C" int a0_service(uint32_t nowMs) {
    if (!s_inited) a0_init();
    if (!tud_hid_ready()) return 0;

    // 1) resposta deferida de leitura (0x16) tem prioridade
    if (s_pending_read != 0xFF) {
        const uint8_t id = s_pending_read;
        uint8_t p[A0_PAYLOAD]; memset(p, 0, sizeof(p));
        p[0] = id; p[1] = 0; p[2] = s_type[id];
        if (s_type[id] == T_FLOAT)      memcpy(&p[3], &s_fval[id], 4);
        else if (s_type[id] == T_U16 || s_type[id] == T_I16) put_i16(&p[3], (int16_t)s_ival[id]);
        else                            p[3] = (uint8_t)(s_ival[id] & 0xFF);
        if (tud_hid_report(A0_RID_SETVALUE, p, A0_PAYLOAD)) { s_pending_read = 0xFF; return 1; }
        return 0;   // EP ocupou entre o ready() e o report — re-tenta no próximo loop
    }

    // 2) telemetria DeviceState (0x21) a ~50Hz (na sobra)
    if ((uint32_t)(nowMs - s_last_state_ms) >= 20) {
        uint8_t p[A0_PAYLOAD];
        a0_build_state(p);
        if (tud_hid_report(A0_RID_STATE, p, A0_PAYLOAD)) { s_last_state_ms = nowMs; return 1; }
    }
    return 0;
}
