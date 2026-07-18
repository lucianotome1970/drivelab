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
#include <cmath>

namespace drivelab {

/// Ganhos/limites de força — espelham os settings da base (BaseSettingId).
struct ForceConfig {
    float totalStrengthPct = 100.0f;  ///< 0..100 força total, o "gain" (TotalStrength)
    float maxTorqueNm      = 2.5f;    ///< torque nominal do motor a 100% (dado do hardware)
    float torqueLimitNm    = 2.5f;    ///< teto DURO de segurança (MaxTorqueLimit; nunca ultrapassar)
    float direction        = 1.0f;    ///< +1 normal, -1 invertido (ForceDirection)
    float linearity        = 1.0f;    ///< curva de resposta |x|^linearity·sinal(x): 1=linear, >1 suaviza o leve, <1 realça o leve
};

/// Efeitos de condição SEMPRE-ativos calculados no device a partir do encoder (somados à força do
/// jogo). Espelham SpringStrength/DamperStrength/StaticDamping — o "feel" base sobre o FFB do jogo.
struct EffectConfig {
    float springNmPerRad       = 0.0f;  ///< mola de centragem: −gain·posição (SpringStrength)
    float damperNmPerRadPerSec = 0.0f;  ///< damper: −gain·velocidade (DamperStrength)
    float frictionNm           = 0.0f;  ///< atrito estático: −sinal(velocidade)·Nm (StaticDamping)
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
/// (Caminho simples; o pipeline M5 completo é computeTorque().)
inline float finalTorque(int32_t hostForce, float positionRad,
                         const ForceConfig& fc, const EndstopConfig& ec) {
    const float t = forceToTorque(hostForce, fc) + endstopTorque(positionRad, ec);
    return clampf(t, -fc.torqueLimitNm, fc.torqueLimitNm);
}

// ---------------------------------------------------------------------------
//  M5 — modelagem de força: curva de resposta + efeitos de condição do device
// ---------------------------------------------------------------------------

/// Curva de resposta (linearidade/gamma): molda o quanto o torque cresce com a força do jogo.
inline float responseCurve(float norm, float linearity) {
    if (linearity == 1.0f) return norm;
    return std::copysign(std::pow(std::fabs(norm), linearity), norm);
}

inline float springTorque(float positionRad, float nmPerRad)         { return -nmPerRad * positionRad; }
inline float damperTorque(float velRadPerSec, float nmPerRadPerSec)  { return -nmPerRadPerSec * velRadPerSec; }

/// Atrito estático: torque constante opondo-se ao movimento (0 se parado).
inline float frictionTorque(float velRadPerSec, float nm) {
    const float eps = 1e-3f;
    if (velRadPerSec >  eps) return -nm;
    if (velRadPerSec < -eps) return  nm;
    return 0.0f;
}

/// Slew-rate: limita a variação de torque por passo (feel + protege a mecânica). maxDelta<=0 desliga.
inline float slewLimit(float target, float prev, float maxDelta) {
    if (maxDelta <= 0.0f) return target;
    return clampf(target, prev - maxDelta, prev + maxDelta);
}

/// Pipeline M5 (sem estado): força do jogo (direção+curva+ganho→Nm) + efeitos do device
/// (mola/damper/atrito, do encoder) + soft-stop, com o teto duro sempre por último.
inline float computeTorque(int32_t hostForce, float positionRad, float velRadPerSec,
                           const ForceConfig& fc, const EffectConfig& ef, const EndstopConfig& ec) {
    float norm = clampf(static_cast<float>(hostForce) / 255.0f * fc.direction, -1.0f, 1.0f);
    norm = responseCurve(norm, fc.linearity);
    float t = norm * (fc.totalStrengthPct / 100.0f) * fc.maxTorqueNm;   // força do jogo → Nm
    t += springTorque(positionRad, ef.springNmPerRad);                  // efeitos always-on (encoder)
    t += damperTorque(velRadPerSec, ef.damperNmPerRadPerSec);
    t += frictionTorque(velRadPerSec, ef.frictionNm);
    t += endstopTorque(positionRad, ec);                                // fim de curso
    return clampf(t, -fc.torqueLimitNm, fc.torqueLimitNm);              // teto duro por último
}

}  // namespace drivelab
