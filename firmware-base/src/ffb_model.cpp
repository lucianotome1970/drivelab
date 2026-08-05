// firmware-base — modelo FFB (STAGE 3b). Dono dos objetos do pipeline puro do
// firmware-base (portados): EffectManager (banco de efeitos PID), ForceReconstructor
// (força do jogo → contínua) e computeTorqueRaw (força+efeitos+endstop → Nm). A cola HID
// alimenta os reports; o laço de 1kHz pede o torque. O ODrive faz a FOC/proteção — aqui
// só a MATEMÁTICA da força (o que resolveu a dor: separar FFB da malha de corrente).
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#include "ffb_model.h"
#include "effect_manager.h"
#include "force_reconstruct.h"
#include "ffb_math.h"
#include "ffb_report.h"
#include "ffb_hid_descriptor.h"
#include "odrive_bridge.h"
#include <cmath>

using namespace drivelab;

static constexpr float k2Pi = 6.28318530718f;

// --- estado do modelo (compartilhado entre a task USB e a task de 1kHz, sem mutex —
//     mesmo padrão provado do firmware-base a 400Hz; ops curtas, torque capado) ---
static EffectManager      s_effects;
static ForceReconstructor s_recon;

// Config de bancada (ajustável pelo DriveLab Studio no Stage 4). Teto subido após a
// leitura por SWD provar que o ODrive entrega o torque pedido a corrente baixa (0,48A
// p/ 0,29Nm) — 1,0Nm era só fraco, não limitado por corrente. Subir com cautela.
static ForceConfig  s_fc = { /*totalStrengthPct*/100.0f, /*maxTorqueNm*/4.0f,
                             /*torqueLimitNm*/5.0f, /*direction*/1.0f, /*linearity*/1.0f, {} };
// SEM mola de centragem sempre-ativa (decisão do usuário 2026-08-03): o FFB é 100% do JOGO,
// como os concorrentes (Moza/Simucube) — sem jogo, o volante fica LIVRE. Isso também tira o
// susto de "runaway/notchy ao girar à mão" (era a mola reagindo à calibração marginal).
// Mantido um damper LEVE só p/ estabilidade do DD livre (não centra, só freia o movimento).
static EffectConfig s_ef = { /*springNmPerRad*/0.0f,      // SEM centragem sempre-ativa (game-driven)
                             /*damperNmPerRadPerSec*/0.03f, // damper leve de estabilidade (não é mola)
                             /*frictionNm*/0.0f };
static EndstopConfig s_ec = { /*rangeRad*/1.4f * k2Pi,  // batente por SW ~±1.4 volta (antes do fundo de escala do eixo)
                              /*stiffnessNm*/8.0f, /*dampingNmPerRadPerSec*/0.05f };

static uint32_t s_nowMs     = 0;
static uint32_t s_lastFfbMs = 0;
static uint8_t  s_deviceGain = 255;
static float    s_telemetryForce = 0.0f;   // força aditiva de telemetria do app (DirectControl 0x10)
static float    s_slewMaxDeltaNm = 0.0f;    // P0 slew: variação máx de torque por tick (0 = off)
static float    s_prevTorque     = 0.0f;    // P0 slew: torque do tick anterior (estado do slewLimit)

// --- Setters do canal A0 (app DriveLab Studio) ---
extern "C" void ffb_model_set_telemetry_force(float f255) { s_telemetryForce = f255; }

// Aplica os settings do app no config do FFB. Defaults do schema reproduzem o config VALIDADO:
// total 100% × maxlimit 80% × 5Nm = 4Nm (maxTorque validado); teto duro 5Nm; damper 10%×0.3=0.03.
extern "C" void ffb_model_set_config(float total_pct, float maxlimit_pct, int direction,
                                     float spring_pct, float damper_pct, int motion_range_deg,
                                     int gspring, int gdamper, int gfriction, int ginertia,
                                     int linearity_pct) {
    const float kFullScaleTorqueNm = 5.0f;
    s_fc.maxTorqueNm   = (total_pct * 0.01f) * (maxlimit_pct * 0.01f) * kFullScaleTorqueNm;
    s_fc.torqueLimitNm = kFullScaleTorqueNm;                        // teto duro fixo (segurança)
    s_fc.direction     = (direction < 0) ? -1.0f : 1.0f;           // flip extra do usuário (base já é kGameForceSign)
    s_ef.springNmPerRad       = (spring_pct * 0.01f) * 0.5f;       // 100% → 0.5 Nm/rad
    s_ef.damperNmPerRadPerSec = (damper_pct * 0.01f) * 0.3f;       // 10% → 0.03 (validado)
    const float half_rad = (float)motion_range_deg * 0.5f * 0.01745329f;  // DOR/2 em rad
    if (half_rad > 0.1f) s_ec.rangeRad = half_rad;
    s_effects.setTypeGains((uint8_t)gspring, (uint8_t)gdamper, (uint8_t)gfriction, (uint8_t)ginertia);
    // P0: LINEARITY (curva de resposta |x|^lin). Setting 50-200% → expoente 0.5-2.0. 100% = linear.
    // A responseCurve já é chamada no computeTorqueRaw; faltava aplicar o setting (era fixo 1.0).
    s_fc.linearity = (linearity_pct >= 10) ? (float)linearity_pct * 0.01f : 1.0f;
}

// Aplica os ajustes avançados (P0). Cada campo com valor válido é aplicado; -1 = mantém o default.
extern "C" void ffb_model_apply_tuning(const FfbTuning* t) {
    if (!t) return;
    // P0: STATIC DAMPING — atrito always-on. frictionTorque já é chamada, mas frictionNm ficava fixo em 0.
    // Setting 0-100% → 0..0.5 Nm de atrito constante. Default do schema 5% = 0,025 Nm (bem leve).
    if (t->static_damping_pct >= 0)
        s_ef.frictionNm = (float)t->static_damping_pct * 0.01f * 0.5f;
    // P0: ENDSTOP DAMPING — amortecimento do batente (não quica). Era fixo 0.05. Setting 0-100% → 0..0.2
    // Nm/rad/s. Default do schema = 25% ≈ 0,05 (sem regressão do anti-bounce atual).
    if (t->endstop_damping_pct >= 0)
        s_ec.dampingNmPerRadPerSec = (float)t->endstop_damping_pct * 0.01f * 0.2f;
    // P0: SLEW-RATE — limita a variação de torque por tick (mata spikes/clunks). slewLimit já existia mas
    // NUNCA era chamada. Setting 0=off; 1-100% → maxDelta = 2/pct Nm/tick (maior % = mais suave). Aplicado
    // no output do compute_torque (loop 1kHz).
    if (t->slew_rate_pct > 0)
        s_slewMaxDeltaNm = 2.0f / (float)t->slew_rate_pct;
    else
        s_slewMaxDeltaNm = 0.0f;
    // P0: FORCE CURVE — curva de força por 5 pontos. applyForceCurve já é chamada, mas s_fc.curve ficava
    // na identidade (0/25/50/75/100). Default = linear → sem regressão. pts[0]<0 = não mexer.
    if (t->curve_pts[0] >= 0) {
        for (int i = 0; i < 5; ++i) {
            int v = t->curve_pts[i];
            s_fc.curve.p[i] = (uint8_t)(v < 0 ? 0 : (v > 100 ? 100 : v));
        }
    }
    // P0: RECONSTRUCTION — janela + LPF do reconstrutor da força constante (s_recon já ativo com defaults).
    // steps 0 = mantém o default 8 (sem regressão); >0 = janela fixa. lpf 0 = off; 1-100% → alpha 0.99..0.10
    // (maior % = mais suave). Era guardado, não aplicado.
    if (t->recon_steps > 0) s_recon.cfg.steps = t->recon_steps;
    s_recon.cfg.lpfAlpha = (t->recon_lpf_pct > 0) ? (1.0f - (float)t->recon_lpf_pct * 0.009f) : 0.0f;
}

// Escala on-wire da força PID (±32767) → escala do engine (±255).
static constexpr float kPidForceToF255 = 255.0f / 32767.0f;

// Sinal GLOBAL da força do jogo no NOSSO frame. -1 CASA com o odrive-wheel
// (result_torque = -forceVector × angle_ratio): a força do jogo é NEGADA relativa ao
// nosso eixo, senão o auto-alinhamento do ACC vira runaway ao soltar e os solavancos
// empurram pro lado errado (incoerência). Ajustável no Stage 4 pelo app.
static constexpr float kGameForceSign = -1.0f;

// Instrumentação p/ leitura por SWD (openocd mdw &g_ffb_dbg) — diagnostica o fluxo
// de força do jogo sem USB/printf. NÃO otimizar: volatile + símbolo global.
volatile int32_t g_ffb_dbg[12] = {0};
// [0]=handle_out calls  [1]=set-constant count  [2]=last constantForce(raw ±32767)
// [3]=last hostF(±255)  [4]=last torque*1000(Nm)  [5]=compute(armed) calls
// [6]=last deviceGain   [7]=last pos*1000(turns) [8]=usedBlocks  [9]=last Iq*1000(A)
// [10]=last dirFactor*1000  [11]=last directed force(±32767)

// Watchdog: 1.0 até 500ms de silêncio; rampa 1→0 em +300ms; 0 depois (segurança).
static float watchdogGain(uint32_t silentMs) {
    if (silentMs <= 500) return 1.0f;
    uint32_t over = silentMs - 500;
    if (over >= 300) return 0.0f;
    return 1.0f - (float)over / 300.0f;
}

extern "C" void ffb_model_advance_clock(uint32_t dms) { s_nowMs += dms; }

extern "C" float ffb_model_compute_torque(float posTurns, float velTurnsPerSec) {
    const float posRad = posTurns * k2Pi;
    const float velRad = velTurnsPerSec * k2Pi;

    float hostF = s_recon.tick();                              // força constante do jogo, reconstruída
    hostF += s_effects.computeForce(posRad, velRad, s_nowMs);  // + efeitos periódicos/condição (Constant é pulado lá)
    hostF *= watchdogGain(s_nowMs - s_lastFfbMs);              // sinal do jogo perdido → decai a 0
    hostF *= (float)s_deviceGain / 255.0f;                     // Device Gain global do host (0x0D)
    hostF += s_telemetryForce;                                 // + efeitos por telemetria do app (aditivo)

    float t = computeTorqueRaw(hostF, posRad, velRad, s_fc, s_ef, s_ec);  // força→Nm + spring/endstop
    t = clampf(t, -s_fc.torqueLimitNm, s_fc.torqueLimitNm);               // TETO DURO por último
    if (s_slewMaxDeltaNm > 0.0f) t = slewLimit(t, s_prevTorque, s_slewMaxDeltaNm);  // P0 slew (0=off)
    s_prevTorque = t;                                                     // estado do slew p/ o próximo tick

    g_ffb_dbg[5]++;
    g_ffb_dbg[3] = (int32_t)hostF;
    g_ffb_dbg[4] = (int32_t)(t * 1000.0f);
    g_ffb_dbg[6] = s_deviceGain;
    g_ffb_dbg[7] = (int32_t)(posTurns * 1000.0f);
    g_ffb_dbg[8] = s_effects.usedBlocks();
    g_ffb_dbg[9] = (int32_t)(odrive_bridge_get_iq_measured() * 1000.0f);
    return t;
}

extern "C" uint8_t ffb_model_create_effect(void) { return s_effects.allocateBlock(); }
extern "C" int     ffb_model_used_blocks(void)   { return s_effects.usedBlocks(); }
extern "C" int     ffb_model_max_blocks(void)    { return kEffectSlots; }
extern "C" void    ffb_model_set_device_gain(uint8_t g) { s_deviceGain = g; }

extern "C" void ffb_model_handle_out(const uint8_t* buf, uint16_t len) {
    if (buf == nullptr || len < 1) return;
    s_lastFfbMs = s_nowMs;                          // qualquer report = jogo ativo (reseta watchdog)
    g_ffb_dbg[0]++;

    s_effects.handleReport(buf, len, s_nowMs);      // roteia 0x01-0x06, 0x0A, 0x0B, 0x0C pro banco

    if (buf[0] == RID_PID_DEVICE_GAIN && len >= 2)  // 0x0D Device Gain global
        s_deviceGain = buf[1];

    if (buf[0] == RID_PID_DEVICE_CONTROL && len >= 2) {   // 0x0C: stop/reset → zera a força constante também
        s_recon.setTarget(0.0f);
    }

    FfbOut o = ffb_parse_out(buf, len);
    if (o.type == FFB_SET_CONSTANT_FORCE) {         // força constante do jogo → reconstrutor (suave)
        // SENTIDO: aplica o campo Direction do Set Effect (axisMagnitudes[0]) — sem isso a
        // força fica com sinal fixo/errado (incoerente + runaway ao soltar). CASADO c/ odrive-wheel.
        const float dir = s_effects.axisDirFactor(o.effectBlock);
        const float directed = (float)o.constantForce * dir * kGameForceSign;
        // FIX de escala: ±32767 (on-wire PID) → ±255 (escala do engine).
        s_recon.setTarget(directed * kPidForceToF255);
        g_ffb_dbg[1]++;
        g_ffb_dbg[2] = o.constantForce;
        g_ffb_dbg[10] = (int32_t)(dir * 1000.0f);
        g_ffb_dbg[11] = (int32_t)directed;
    }
}
