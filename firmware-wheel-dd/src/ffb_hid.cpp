// firmware-wheel-dd — cola HID (STAGE 3a): TinyUSB HID ↔ DriveLab.
//
// Faz o device virar CONTROLE DE JOGO:
//   - hid_send_joystick(): monta o RID_JOYSTICK (8 botões + 8 eixos int16) a partir
//     do encoder (via odrive_bridge) → o eixo X é a DIREÇÃO. Chamado pelo laço.
//   - tud_hid_get_report_cb / tud_hid_set_report_cb: o handshake PID mínimo que o
//     DirectInput exige pra reconhecer o dispositivo como Force-Feedback (PID State,
//     Create New Effect → aloca bloco, Block Load / Pool respondem o pool).
//
// STAGE 3a NÃO gera força ainda (só torna o controle visível + direção viva). O
// roteamento de efeitos → torque pela ponte entra no 3b (effect_manager/reconstruct).
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#include <stdint.h>
#include <string.h>
#include "tusb.h"
#include "ffb_hid_descriptor.h"
#include "pid_state.h"
#include "odrive_bridge.h"
#include "ffb_model.h"

// Canal A0 (app DriveLab Studio) — trata os OUT reports vendor (a0_channel.cpp).
extern "C" void a0_handle_out(const uint8_t* buf, uint16_t len);

// --- Report de Input do RID_JOYSTICK (idêntico ao firmware-base): 8 botões + 8 eixos x16b = 24 bytes ---
typedef struct __attribute__((packed)) {
    uint8_t buttons[8];
    int16_t axes[8];
} JoystickInputReport;
_Static_assert(sizeof(JoystickInputReport) == 24, "RID_JOYSTICK deve ter 24 bytes");

// Curso do volante mapeado pro fundo de escala do eixo (turns). ±1.5 turns (=540°)
// por enquanto; o curso real (DOR) vem da config no Stage 4.
static const float kSteerRangeTurns = 1.5f;

// Bloco alocado no último Create New Effect (p/ o Block Load responder). O banco
// real de efeitos vive no ffb_model (EffectManager, com reuso de blocos livres).
static uint8_t s_last_effect_block = 0;

// Monta e envia o RID_JOYSTICK com a direção lida do encoder. Chamar quando o
// endpoint IN do HID estiver livre (tud_hid_ready()). Retorna true se enfileirou.
extern "C" int hid_send_joystick(void) {
    if (!tud_hid_ready()) return 0;
    JoystickInputReport rep;
    memset(&rep, 0, sizeof(rep));

    float pos = odrive_bridge_get_pos_turns();     // turns acumulados do encoder
    float norm = pos / kSteerRangeTurns;           // -1..+1 no curso
    if (norm > 1.0f) norm = 1.0f; else if (norm < -1.0f) norm = -1.0f;
    rep.axes[0] = (int16_t)(norm * 32767.0f);      // eixo X = direção

    return tud_hid_report(RID_JOYSTICK, &rep, sizeof(rep)) ? 1 : 0;
}

// ---------------------------------------------------------------------------
// SET_REPORT (host → device): Feature = Create New Effect / Output = efeitos.
// Stage 3b: roteia pro ffb_model (EffectManager + ForceReconstructor) → torque.
// ---------------------------------------------------------------------------
extern "C" void tud_hid_set_report_cb(uint8_t instance, uint8_t report_id,
                                      hid_report_type_t report_type,
                                      uint8_t const* buffer, uint16_t bufsize) {
    (void)instance;
    // TinyUSB entrega o report_id em `report_id` quando != 0; se 0, o 1º byte do buffer é o ID.
    uint8_t rid = report_id;
    const uint8_t* buf = buffer;
    uint16_t len = bufsize;
    if (rid == 0 && len > 0) { rid = buffer[0]; buf = buffer; }

    if (report_type == HID_REPORT_TYPE_FEATURE) {
        if (rid == RID_PID_CREATE_NEW_EFFECT)
            s_last_effect_block = ffb_model_create_effect();   // reserva o 1º bloco livre (1-based)
        return;
    }

    // OUTPUT reports: canal A0 do app (0x10 DIRECT / 0x14 SETWRITE / 0x15 SETREAD / 0x22 CMD) vs
    // FFB do jogo (0x01-0x06,0x0A-0x0D...). Despacha por report ID (buf[0]=rid quando veio pelo EP OUT).
    if (rid == 0x10 || rid == 0x14 || rid == 0x15 || rid == 0x22) {
        a0_handle_out(buf, len);
    } else {
        ffb_model_handle_out(buf, len);
    }
}

// ---------------------------------------------------------------------------
// GET_REPORT (device → host): PID State, Block Load, Pool — o que o DirectInput
// consulta pra achar que o dispositivo é FFB e tem onde alocar efeitos.
// ---------------------------------------------------------------------------
extern "C" uint16_t tud_hid_get_report_cb(uint8_t instance, uint8_t report_id,
                                          hid_report_type_t report_type,
                                          uint8_t* buffer, uint16_t reqlen) {
    (void)instance;

    if (report_type == HID_REPORT_TYPE_INPUT && report_id == RID_PID_STATE) {
        if (reqlen < 1) return 0;
        buffer[0] = buildPidStateByte(false /*devicePaused*/, true /*actuatorsEnabled*/,
                                      true /*safetySwitch*/, false /*actuatorOverride*/,
                                      true /*actuatorPower*/);
        return 1;
    }

    if (report_type != HID_REPORT_TYPE_FEATURE) return 0;

    // Block Load (Feature, 0x12) — 4 bytes, CASADO com o m5 provado (ACC ok):
    //   [block, status(1=Success/2=Full), ramPoolAvail_lo, ramPoolAvail_hi]. Budget 16 B/slot.
    if (report_id == RID_PID_BLOCK_LOAD) {
        if (reqlen < 4) return 0;
        buffer[0] = s_last_effect_block;
        buffer[1] = (s_last_effect_block != 0) ? 1 : 2;   // 2 = pool cheio (bloco 0)
        uint16_t avail = (uint16_t)((ffb_model_max_blocks() - ffb_model_used_blocks()) * 16);
        buffer[2] = (uint8_t)(avail & 0xFF);
        buffer[3] = (uint8_t)(avail >> 8);
        return 4;
    }

    // Pool (Feature, 0x13) — 4 bytes: [ramPoolSize_lo, hi, simultaneousMax, flags].
    if (report_id == RID_PID_POOL) {
        if (reqlen < 4) return 0;
        uint16_t poolSize = (uint16_t)(ffb_model_max_blocks() * 16);
        buffer[0] = (uint8_t)(poolSize & 0xFF);
        buffer[1] = (uint8_t)(poolSize >> 8);
        buffer[2] = 8;      // efeitos simultâneos (Device Managed Pool)
        buffer[3] = 0;
        return 4;
    }

    return 0;
}
