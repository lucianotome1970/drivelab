// ============================================================================
//  DriveLab Firmware
//  ffb_math.h — Matemática FFB PURA (força→torque, soft-stop, corte de corrente). Sem deps de HW.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença LGPL-3.0
// ============================================================================
//
// Núcleo do "cérebro" FFB — funções PURAS, independentes de plataforma (sem Arduino,
// sem STM32, sem SimpleFOC). Compilam tanto no firmware quanto num alvo de teste no PC
// (ver firmware-base/test/). É aqui que mora a correção matemática força→torque, testável
// sem placa nenhuma. As bordas de hardware ficam em hal.h; a orquestração em ffb_controller.h.
#pragma once

#include <cstdint>

namespace drivelab {

/// Ganhos/limites de força — espelham os settings da base (BaseSettingId.TotalStrength, limites).
struct ForceConfig {
    float totalStrengthPct = 100.0f;  ///< 0..100 (força total, o "gain" do usuário)
    float maxTorqueNm      = 2.5f;    ///< torque nominal do motor a 100% (dado do hardware)
    float torqueLimitNm    = 2.5f;    ///< teto DURO de segurança (nunca ultrapassar)
};

/// Fim de curso por software (soft-stop): mola que empurra de volta além da faixa.
struct EndstopConfig {
    float rangeRad    = 4.71238898f;  ///< meia-faixa (ex.: ±270° = 4,712 rad)
    float stiffnessNm = 3.0f;         ///< Nm por rad de invasão além da faixa
};

inline float clampf(float v, float lo, float hi) {
    return v < lo ? lo : (v > hi ? hi : v);
}

/// Força FFB do host [-255,255] → torque (Nm): normaliza, aplica força total e o teto de segurança.
inline float forceToTorque(int32_t hostForce, const ForceConfig& c) {
    const float norm = clampf(static_cast<float>(hostForce) / 255.0f, -1.0f, 1.0f);
    const float nm   = norm * (c.totalStrengthPct / 100.0f) * c.maxTorqueNm;
    return clampf(nm, -c.torqueLimitNm, c.torqueLimitNm);
}

/// Soft-stop: dentro da faixa = 0; além dela, torque de mola proporcional à invasão (sinal contrário).
inline float endstopTorque(float positionRad, const EndstopConfig& e) {
    if (positionRad >  e.rangeRad) return -(positionRad - e.rangeRad) * e.stiffnessNm;
    if (positionRad < -e.rangeRad) return -(positionRad + e.rangeRad) * e.stiffnessNm;
    return 0.0f;
}

/// Corte de segurança por sobrecorrente: true se qualquer fase excede ±limitA.
inline bool overCurrent(float ia, float ib, float ic, float limitA) {
    return  ia >  limitA || ia < -limitA ||
            ib >  limitA || ib < -limitA ||
            ic >  limitA || ic < -limitA;
}

/// Torque final seguro = força do host + soft-stop, sempre reclampado ao teto duro por último.
inline float finalTorque(int32_t hostForce, float positionRad,
                         const ForceConfig& fc, const EndstopConfig& ec) {
    const float t = forceToTorque(hostForce, fc) + endstopTorque(positionRad, ec);
    return clampf(t, -fc.torqueLimitNm, fc.torqueLimitNm);
}

}  // namespace drivelab
