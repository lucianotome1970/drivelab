// firmware-wheel-dd — modelo FFB (STAGE 3b): API C plana sobre o pipeline puro do
// firmware-base (EffectManager + ForceReconstructor + computeTorqueRaw), portado.
// A cola HID (ffb_hid.cpp, contexto USB) roteia os reports OUT/Feature pra cá; o laço
// de 1kHz (ffb_task.cpp) pede o torque. Converte força do jogo → Nm, escrito na ponte.
// Autor: Luciano Tomé <lucianotome1970@gmail.com> — Licença MIT
#pragma once
#include <stdint.h>
#ifdef __cplusplus
extern "C" {
#endif

// --- laço (ffb_task, 1kHz) ---
void  ffb_model_advance_clock(uint32_t dms);                 // avança o relógio do engine (ms)
float ffb_model_compute_torque(float posTurns, float velTurnsPerSec);  // roda o pipeline → torque Nm (com teto)

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
                             int gspring, int gdamper, int gfriction, int ginertia);

#ifdef __cplusplus
}
#endif
