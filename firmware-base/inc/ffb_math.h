// ============================================================================
//  DriveLab Firmware
//  ffb_math.h — Matemática FFB PURA (força→torque, soft-stop, corte de corrente). Sem deps de HW.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
//
// Núcleo do "cérebro" FFB — funções PURAS, independentes de plataforma (sem Arduino,
// sem STM32, sem dependencia de driver de motor). Compilam tanto no firmware quanto num alvo de teste no PC
// (ver firmware-base/test/). É aqui que mora a correção matemática força→torque, testável
// sem placa nenhuma. As bordas de hardware ficam em hal.h; a orquestração em ffb_controller.h.
#pragma once

#include <cstdint>
#include <cmath>

namespace drivelab {

/// Curva de resposta POR PONTOS da força do jogo: 11 pontos fixos na entrada (0/10/.../100%) com a saída
/// em 0..100%. Molda o feel com muito mais liberdade que a `linearity` (que é um gamma só). Aplicada ao
/// MÓDULO da força, preservando o sinal (o FFB é bidirecional e simétrico). Default = linear (identidade),
/// então quem não mexer não sente diferença nenhuma.
struct ForceCurve {
    /// ONZE pontos, de 10 em 10% da entrada. Eram cinco (de 25 em 25) e o meio da escala — 25% a
    /// 75%, que é onde se dirige a maior parte de uma volta — tinha UM único ponto de controle:
    /// não dava para levantar o começo do meio sem levantar o fim junto.
    static constexpr int kPontos = 11;
    uint8_t p[kPontos] = {0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100};

    /// Inclinacao em cada ponto, para a interpolacao SUAVE. Calculada em prepare(), nao a cada
    /// amostra: o laco de FFB roda a 1 kHz e a curva muda so quando alguem salva.
    float m[kPontos] = {0};

    /// Curva default (linear)? Nesse caso o pipeline pula o cálculo inteiro.
    bool isLinear() const {
        for (int i = 0; i < kPontos; ++i)
            if (p[i] != static_cast<uint8_t>(i * 10)) return false;
        return true;
    }

    /// Recalcula as inclinacoes. CHAMAR sempre que `p` mudar.
    ///
    /// POR QUE SUAVE, E POR QUE ESTA SUAVE EM PARTICULAR
    ///
    /// Ligar os pontos com retas faz de cada ponto um CANTO, e canto na curva de forca vira degrau
    /// no volante: a forca muda de inclinacao de um tick para o outro. Aumentar o numero de pontos
    /// nao resolve — so deixa os cantos menores e a tela impossivel de ajustar.
    ///
    /// A regra de Fritsch-Carlson (PCHIP) e escolhida porque PRESERVA A MONOTONICIDADE: uma spline
    /// comum "estufa" entre os pontos e pode criar uma barriga que DESCE, que e exatamente o que a
    /// tela proibe ao arrastar ("pedi mais forca e recebi menos"). Aqui isso e impossivel por
    /// construcao: onde a secante troca de sinal a inclinacao vira zero, e nos demais a media
    /// harmonica nunca ultrapassa o dobro da menor secante vizinha.
    void prepare() {
        // O PRIMEIRO PONTO E SEMPRE ZERO, e isto NAO e cosmetico. A curva e aplicada ao modulo da
        // forca preservando o sinal; com p[0] = 40, um pedido de +0,001 entrega +40% e um de -0,001
        // entrega -40% — um salto de 80% do fundo de escala toda vez que a forca do jogo cruza o
        // centro, e forca constante no volante com o carro parado. A tela ja trava o ponto; aqui e a
        // segunda barreira, para um blob corrompido nao produzir isso.
        p[0] = 0;

        const float h = 1.0f / static_cast<float>(kPontos - 1);
        float d[kPontos - 1];
        for (int i = 0; i < kPontos - 1; ++i)
            d[i] = (static_cast<float>(p[i + 1]) - static_cast<float>(p[i])) / h;

        m[0] = d[0];
        m[kPontos - 1] = d[kPontos - 2];
        for (int i = 1; i < kPontos - 1; ++i) {
            if (d[i - 1] * d[i] <= 0.0f) {
                m[i] = 0.0f;                                  // troca de sentido (ou trecho plano)
            } else {
                m[i] = 2.0f * d[i - 1] * d[i] / (d[i - 1] + d[i]);   // media harmonica
            }
        }
    }
};

/// Interpola a curva em `norm` (-1..1), SUAVE (Hermite cubico com as inclinacoes de prepare()).
/// Sem cantos: e o mesmo desenho que o app mostra.
inline float applyForceCurve(float norm, const ForceCurve& c) {
    if (c.isLinear()) return norm;

    float a = std::fabs(norm);
    if (a > 1.0f) a = 1.0f;

    const float h = 1.0f / static_cast<float>(ForceCurve::kPontos - 1);
    const float x = a * (ForceCurve::kPontos - 1);   // 0..10 → posição entre os 11 pontos
    int i = static_cast<int>(x);
    if (i > ForceCurve::kPontos - 2) i = ForceCurve::kPontos - 2;   // último segmento quando a == 1
    const float t = x - static_cast<float>(i);

    const float t2 = t * t, t3 = t2 * t;
    const float h00 =  2.0f * t3 - 3.0f * t2 + 1.0f;
    const float h10 =         t3 - 2.0f * t2 + t;
    const float h01 = -2.0f * t3 + 3.0f * t2;
    const float h11 =         t3 -        t2;

    const float y = (h00 * static_cast<float>(c.p[i])     + h10 * h * c.m[i] +
                     h01 * static_cast<float>(c.p[i + 1]) + h11 * h * c.m[i + 1]) / 100.0f;

    return norm < 0.0f ? -y : y;
}

/// Ganhos/limites de força — espelham os settings da base (BaseSettingId).
struct ForceConfig {
    float totalStrengthPct = 100.0f;  ///< 0..100 força total, o "gain" (TotalStrength)
    float maxTorqueNm      = 2.5f;    ///< torque nominal do motor a 100% (dado do hardware)
    float torqueLimitNm    = 2.5f;    ///< teto DURO de segurança (MaxTorqueLimit; nunca ultrapassar)
    float direction        = 1.0f;    ///< +1 normal, -1 invertido (ForceDirection)
    float linearity        = 1.0f;    ///< curva de resposta |x|^linearity·sinal(x): 1=linear, >1 suaviza o leve, <1 realça o leve
    ForceCurve curve{};               ///< curva por pontos aplicada DEPOIS da linearity (default = identidade)
};

/// Efeitos de condição SEMPRE-ativos calculados no device a partir do encoder (somados à força do
/// jogo). Espelham SpringStrength/DamperStrength/StaticDamping — o "feel" base sobre o FFB do jogo.
struct EffectConfig {
    float springNmPerRad       = 0.0f;  ///< mola de centragem: −gain·posição (SpringStrength)
    float damperNmPerRadPerSec = 0.0f;  ///< damper: −gain·velocidade (DamperStrength)
    float frictionNm           = 0.0f;  ///< atrito estático: −sinal(velocidade)·Nm (StaticDamping)
};

/// Fim de curso por software (soft-stop): mola que empurra de volta além da faixa + amortecimento
/// que ABSORVE o impacto (senão o volante quica no batente). É o "end-of-travel damping" das bases top.
struct EndstopConfig {
    float rangeRad             = 4.71238898f; ///< meia-faixa (ex.: ±270° = 4,712 rad)
    float stiffnessNm          = 3.0f;        ///< Nm por rad de invasão além da faixa (a "parede")
    float dampingNmPerRadPerSec = 0.0f;       ///< amortecimento no batente: absorve a energia → não quica
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

/// Soft-stop (mola): dentro da faixa = 0; além dela, torque proporcional à invasão (sinal contrário).
inline float endstopTorque(float positionRad, const EndstopConfig& e) {
    if (positionRad >  e.rangeRad) return -(positionRad - e.rangeRad) * e.stiffnessNm;
    if (positionRad < -e.rangeRad) return -(positionRad + e.rangeRad) * e.stiffnessNm;
    return 0.0f;
}

/// Amortecimento de fim de curso: SÓ na região do batente, opõe a velocidade (−D·ω). Absorve a
/// energia do impacto → o volante encosta e assenta em vez de quicar. 0 dentro da faixa.
inline float endstopDamping(float positionRad, float velRadPerSec, const EndstopConfig& e) {
    if (positionRad > e.rangeRad || positionRad < -e.rangeRad)
        return -e.dampingNmPerRadPerSec * velRadPerSec;
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

/// Escala de velocidade (rad/s) da suavização do atrito estático perto de v=0 (evita chatter). AJUSTAR
/// na bancada: menor = transição mais "seca"/rápida (mais perto do Coulomb ideal), maior = zona morta maior.
static constexpr float kFrictionSmoothVel = 0.3f;

/// Atrito estático: torque ~constante opondo-se ao movimento.
/// vScale<=0 → Coulomb "duro" (degrau no zero). vScale>0 → Coulomb REGULARIZADO por tanh: a força cresce
/// suavemente de 0 até ±nm ao longo de ~vScale, matando o chatter que o degrau causa quando a velocidade
/// estimada oscila em torno de zero (mãos paradas). Satura em ±nm bem antes de 1 rad/s com o default.
inline float frictionTorque(float velRadPerSec, float nm, float vScale = 0.0f) {
    if (vScale > 0.0f) return -nm * std::tanh(velRadPerSec / vScale);
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
/// hostForce em [-255,255] como FLOAT — aceita a força já reconstruída (contínua, não só o inteiro).
/// Igual ao computeTorque, mas SEM o teto duro final — devolve a demanda "crua" de torque (o quanto o jogo +
/// efeitos pediram). Serve para MEDIR clipping (demanda crua vs teto): se |raw| > torqueLimitNm, cortou.
inline float computeTorqueRaw(float hostForce, float positionRad, float velRadPerSec,
                              const ForceConfig& fc, const EffectConfig& ef, const EndstopConfig& ec) {
    float norm = clampf(hostForce / 255.0f * fc.direction, -1.0f, 1.0f);
    norm = responseCurve(norm, fc.linearity);
    norm = applyForceCurve(norm, fc.curve);                             // curva por pontos (default: identidade)
    float t = norm * (fc.totalStrengthPct / 100.0f) * fc.maxTorqueNm;   // força do jogo → Nm
    t += springTorque(positionRad, ef.springNmPerRad);                  // efeitos always-on (encoder)
    t += damperTorque(velRadPerSec, ef.damperNmPerRadPerSec);
    t += frictionTorque(velRadPerSec, ef.frictionNm, kFrictionSmoothVel);   // Coulomb suave (sem chatter no zero)
    t += endstopTorque(positionRad, ec);                                // fim de curso (mola)
    t += endstopDamping(positionRad, velRadPerSec, ec);                 // + amortecimento (não quica)
    return t;                                                           // SEM teto (para medir clipping)
}

inline float computeTorque(float hostForce, float positionRad, float velRadPerSec,
                           const ForceConfig& fc, const EffectConfig& ef, const EndstopConfig& ec) {
    return clampf(computeTorqueRaw(hostForce, positionRad, velRadPerSec, fc, ef, ec),
                  -fc.torqueLimitNm, fc.torqueLimitNm);                 // teto duro por último
}

}  // namespace drivelab
