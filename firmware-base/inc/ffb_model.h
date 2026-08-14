// firmware-base — modelo FFB (STAGE 3b): API C plana sobre o pipeline puro do
// firmware-base (EffectManager + ForceReconstructor + computeTorqueRaw), portado.
// A cola HID (ffb_hid.cpp, contexto USB) roteia os reports OUT/Feature pra cá; o laço
// de 1kHz (ffb_task.cpp) pede o torque. Converte força do jogo → Nm, escrito na ponte.
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif

// ---------------------------------------------------------------------------------------------
// FUNDO DE ESCALA DO TORQUE — a referência dos percentuais do app
//
// "Força total 100%" e "Limite máximo 80%" na tela não são percentuais de nada visível: são
// percentuais DESTE número. 100% × 80% × 15 Nm = 12 Nm de trabalho.
//
// Histórico: 5 Nm até 2026-08-05, quando a bancada mostrou que o conjunto usava só ~18% do que
// entrega (pico real de 2,46 Nm com 4,5 A dos 25 disponíveis) → 10 Nm. Subiu para 15 em
// 2026-08-11, a pedido do usuário, depois de a pista acusar 38% de clipping com o FFB fraco.
//
// ⚠️ TRÊS TETOS REAIS ficam ABAIXO deste número — ele é o fundo da régua, não uma promessa:
//   1. current_lim (25 A) × Kt (0,55 Nm/A) = ~13,7 Nm. Acima disso a corrente satura antes.
//   2. o Kt de 0,55 é de CATÁLOGO, nunca foi medido nesta bancada — se o real for menor, o
//      torque entregue é proporcionalmente menor do que o firmware acredita estar mandando.
//   3. calor. Não há sensor de temperatura no motor (o firmware reporta -128), logo NÃO HÁ corte
//      térmico: quem protege o motor é a mão da pessoa nele.
//
// Este é o ÚNICO lugar onde a escala é definida. A telemetria de torque (a0_channel) divide por
// ela para mandar a fração ao app — se os dois valores se separarem, a barra do app mente.
// ---------------------------------------------------------------------------------------------
#define DRVLAB_FULL_SCALE_TORQUE_NM 15.0f

// --- laço (ffb_task, 1kHz) ---
void  ffb_model_advance_clock(uint32_t dms);                 // avança o relógio do engine (ms)
float ffb_model_compute_torque(float posTurns, float velTurnsPerSec);  // roda o pipeline → torque Nm (com teto)

// Clipping do FFB: fração do tempo (janela de 500 ms) em que a força pedida saturou, em 0-255.
// Só é medido com o motor armado — o laço chama o reset enquanto ele estiver desarmado.
uint8_t ffb_model_get_clipping(void);
// As duas parcelas do clipping, separadas — só uma delas responde a ajuste NOSSO:
//   game = o jogo já mandou a força cortada (baixar o ganho DO JOGO é o único remédio)
//   base = o teto de torque cortou o pedido (mais torque, ou menos ganho)
uint8_t ffb_model_get_clipping_game(void);
uint8_t ffb_model_get_clipping_base(void);
// Fracao do tempo ARMADO em que a base saturou (0-255). Nao e pico de janela: e razao direta entre
// ticks, para que "3%" signifique "3% do tempo que a pessoa dirigiu".
uint8_t ffb_model_get_clipping_session(void);
void    ffb_model_reset_clipping(void);

/// Zera o estado TRANSITÓRIO do pipeline — o que é "memória do último torque emitido": o valor
/// anterior do limitador de variação, o filtro de saída e o reconstrutor de força.
///
/// POR QUE EXISTE: com a base desarmada o ffb_model_compute_torque() não é chamado, então esse
/// estado congela no último valor de antes do desarme. No re-arme o slewLimit() partia dali —
/// `prev ± maxDelta` — e o primeiro tick saía perto do torque VELHO em vez de zero. Como o "Salvar
/// no controlador" desarma e re-arma, um tranco no salvamento seria o sintoma.
///
/// NÃO mexe nos slots de efeito nem no device gain: esses pertencem ao host, que continua achando
/// que os criou. Limpá-los deixaria o jogo sem efeitos até ele reenviá-los — e ele não reenvia.
void    ffb_model_reset_transient(void);

// Controle direto do app (report 0x10). `constant`/`periodic` sao FORCA -1..1 (o app ja calculou a
// forma de onda); `spring`/`damper` sao GANHO 0..1 (dependem de posicao e velocidade, calculadas no
// firmware). Renova o watchdog: sem novos envios a forca decai sozinha em ~800 ms.
void ffb_model_set_direct_control(float constant, float spring, float periodic, float damper);

// --- roteamento HID (ffb_hid.cpp, contexto USB) ---
uint8_t ffb_model_create_effect(void);                       // Create New Effect → bloco 1-based (0 = pool cheio)
void    ffb_model_handle_out(const uint8_t* buf, uint16_t len); // roteia um OUT report (efeitos/força/device ctrl)
void    ffb_model_set_device_gain(uint8_t g);                // Device Gain global (0x0D)
int     ffb_model_used_blocks(void);                         // blocos reservados (p/ Block Load / Pool)
int     ffb_model_max_blocks(void);

// --- canal A0 (app DriveLab Studio) ---
void    ffb_model_set_telemetry_force(float f255);           // DirectControl 0x10 (aditivo)
void    ffb_model_set_config(float total_pct, float maxlimit_pct, int direction,
                             float spring_pct, float damper_pct, int motion_range_deg,
                             int gspring, int gdamper, int gfriction, int ginertia,
                             int linearity_pct);

// Ajustes "avançados" (P0) aplicados no ffb_model. A struct CRESCE conforme os settings vão sendo ligados
// (um por commit) — evita explodir os parâmetros do set_config. -1 num campo = "não mexer" (mantém default).
typedef struct {
    int static_damping_pct;   // setting 6 — atrito always-on (frictionNm)
    int endstop_damping_pct;  // setting 23 — amortecimento do batente (s_ec.damping)
    int slew_rate_pct;        // setting 26 — limite de variação de torque por tick (0 = off)
    int curve_pts[11];        // curva de força por 11 pontos, de 10 em 10% (default 0/10/.../100 = linear).
                              // Ids 28-32 (os cinco primeiros) e 49-54 (os seis novos). pts[0]<0 = não mexer
    int recon_steps;          // setting 19 — janela do reconstrutor (0 = mantém default 8)
    int recon_lpf_pct;        // setting 20 — LPF extra do reconstrutor (0 = off)
    int soft_stop_range_deg;  // setting 1 — quantos graus ANTES do fim do curso a parede começa (<0 = não mexer)
    int soft_stop_strength_pct; // setting 2 — rigidez da parede, 0-100% → 0..10 Nm/rad (<0 = não mexer)
    int output_filter_hz;     // setting 21 — corte do filtro de saída da força, em Hz (0 = off/bypass)
    int osc_guard_enabled;    // setting 22 — guarda de oscilação (0 = off). Ver inc/osc_guard.h:
                              // amortece SÓ durante um episódio de auto-oscilação, ao contrário do
                              // damper, que é sempre ativo e come textura de pista o tempo todo.
} FfbTuning;
void    ffb_model_apply_tuning(const FfbTuning* t);

#ifdef __cplusplus
}
#endif
