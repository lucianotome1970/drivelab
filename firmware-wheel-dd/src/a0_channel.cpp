// firmware-wheel-dd — canal A0 (STAGE 4, Marco A): telemetria DeviceState pro DriveLab Studio.
//
// O app fala com a base por um canal HID vendor (usage page 0xFF00, ver a0_hid_descriptor.h),
// separado do joystick/FFB (0x01). Aqui montamos e enviamos o report DeviceState (RID 0x21) com
// o ÂNGULO do volante (é o que gira o volante na tela do app) + telemetria (posição, corrente,
// tensão do bus, flags). Layout casado byte-a-byte com DriveLab.Core/Protocol/BaseState.cs.
//
// Marco A = só telemetria (app reconhece + mostra o volante girando). Settings (0x14/0x15/0x16)
// e comandos (0x22/0x10) entram no Marco B.
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#include <stdint.h>
#include <string.h>
#include "tusb.h"
#include "odrive_bridge.h"

#define A0_RID_STATE   0x21   // Input: DeviceState (ver a0_hid_descriptor.h)
#define A0_REPORT_SIZE 63     // payload (ReportConstants.ReportSize no app)

// Curso do volante → fundo de escala (igual ao eixo do joystick em ffb_hid.cpp).
static const float kSteerRangeTurns = 1.5f;

static inline void put_i16(uint8_t* p, int16_t v)  { p[0] = (uint8_t)(v & 0xFF); p[1] = (uint8_t)((v >> 8) & 0xFF); }
static inline void put_u16(uint8_t* p, uint16_t v) { p[0] = (uint8_t)(v & 0xFF); p[1] = (uint8_t)((v >> 8) & 0xFF); }

// Monta + envia o DeviceState (0x21). Chamar quando o EP IN do HID estiver livre.
// Retorna true se enfileirou.
extern "C" int a0_send_device_state(void) {
    if (!tud_hid_ready()) return 0;

    uint8_t p[A0_REPORT_SIZE];
    memset(p, 0, sizeof(p));

    // [0:4] FirmwareVersion = ReleaseType, Major, Minor, Patch
    p[0] = 0;  p[1] = 0;  p[2] = 3;  p[3] = 0;    // dev 0.3.0 (firmware-wheel-dd, Stage 3/4)

    // [4] Flags: ForceEnabled(1)+Calibrated(2) quando armado; Error(4) se houver erro de eixo.
    uint8_t flags = 0;
    if (odrive_bridge_motor_is_armed()) flags |= 0x01 | 0x02;   // ForceEnabled + Calibrated
    if (odrive_bridge_axis_error() != 0) flags |= 0x04;          // Error
    p[4] = flags;

    const float turns = odrive_bridge_get_pos_turns();

    // [5:7] Position: normalizado ±32767 no curso (= eixo do joystick)
    float norm = turns / kSteerRangeTurns;
    if (norm > 1.0f) norm = 1.0f; else if (norm < -1.0f) norm = -1.0f;
    put_i16(&p[5], (int16_t)(norm * 32767.0f));

    // [7:9] AngleDeciDeg: ângulo em décimos de grau (turns × 360 × 10) — GIRA O VOLANTE NA TELA
    float decideg = turns * 3600.0f;
    if (decideg > 32767.0f) decideg = 32767.0f; else if (decideg < -32767.0f) decideg = -32767.0f;
    put_i16(&p[7], (int16_t)decideg);

    // [9:11] Torque: torque comandado em centi-Nm (Nm × 100)
    put_i16(&p[9], (int16_t)(odrive_bridge_get_input_torque() * 100.0f));

    // [11:13] MotorCurrentMa: Iq medido em mA
    put_i16(&p[11], (int16_t)(odrive_bridge_get_iq_measured() * 1000.0f));

    // [13] FetTempC — sem sensor por ora (0)
    // [14] ErrorCode: byte baixo do erro do eixo
    p[14] = (uint8_t)(odrive_bridge_axis_error() & 0xFF);

    // [15:17] BusVoltageMv: tensão do bus em mV
    put_u16(&p[15], (uint16_t)(odrive_bridge_get_vbus() * 1000.0f));

    // [17] MotorTempC, [18] McuTempC, [19] Clipping — 0 por ora (Marco B/telemetria completa)

    return tud_hid_report(A0_RID_STATE, p, A0_REPORT_SIZE) ? 1 : 0;
}
