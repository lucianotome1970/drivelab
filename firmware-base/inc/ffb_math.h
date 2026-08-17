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
    /// ⚠️ O MURO — o fim do curso de verdade, e o que faz o volante PARAR em 450° em vez de ir a 459°.
    ///
    /// A rampa acima (de rangeRad até aqui) existe para a aproximação não ser um soco: ela é macia de
    /// propósito. Só que macia ela também é vencível — a 10 Nm/rad, empurrar 9° custa 1,6 Nm, e
    /// qualquer braço passa. O batente virava uma sugestão.
    ///
    /// As bases de referência param redondo no curso configurado, e é isso que se espera de um fim de
    /// curso: a rampa amortece a chegada, o muro não deixa passar. Rigidez alta aqui não deixa o
    /// batente áspero, porque quem chega já passou pela rampa.
    float wallRad              = 0.0f;        ///< fim do curso (DOR/2). 0 = sem muro
    float wallStiffnessNm      = 0.0f;        ///< Nm por rad ALÉM do fim do curso — bem maior que a rampa
    float wallDampingNmPerRadPerSec = 0.0f;   ///< amortecimento do muro, só na ENTRADA (ver endstopDamping)
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

/// Soft-stop em DOIS estágios: a rampa macia (da faixa até o fim do curso) para a chegada não ser um
/// soco, e o MURO (além do fim do curso) para o volante não passar. Dentro da faixa = 0.
inline float endstopTorque(float positionRad, const EndstopConfig& e) {
    float t = 0.0f;
    if (positionRad >  e.rangeRad)      t = -(positionRad - e.rangeRad) * e.stiffnessNm;
    else if (positionRad < -e.rangeRad) t = -(positionRad + e.rangeRad) * e.stiffnessNm;
    // O muro SOMA à rampa, e não a substitui: a força cresce de forma contínua ao cruzar o fim do
    // curso. Substituir criaria um degrau de torque exatamente no ponto de maior velocidade.
    if (e.wallRad > 0.0f) {
        if (positionRad >  e.wallRad)      t -= (positionRad - e.wallRad) * e.wallStiffnessNm;
        else if (positionRad < -e.wallRad) t -= (positionRad + e.wallRad) * e.wallStiffnessNm;
    }
    return t;
}

/// Amortecimento de fim de curso: SÓ na região do batente, opõe a velocidade (−D·ω). Absorve a
/// energia do impacto → o volante encosta e assenta em vez de quicar. 0 dentro da faixa.
inline float endstopDamping(float positionRad, float velRadPerSec, const EndstopConfig& e) {
    float t = 0.0f;
    if (positionRad > e.rangeRad || positionRad < -e.rangeRad)
        t = -e.dampingNmPerRadPerSec * velRadPerSec;
    // ⚠️ O AMORTECIMENTO DO MURO AGE SÓ NA ENTRADA — quando o volante está AFUNDANDO nele.
    //
    // Amortecer nos dois sentidos é o que produz o repique que já vimos na bancada: na volta, o
    // amortecimento freia a saída, a mola continua empurrando, e os dois brigam num ponto onde a
    // soma já está saturada no teto. Freando só quem entra, o volante encosta, para, e sai limpo.
    if (e.wallRad > 0.0f && e.wallDampingNmPerRadPerSec > 0.0f) {
        const bool afundando = (positionRad >  e.wallRad && velRadPerSec > 0.0f) ||
                               (positionRad < -e.wallRad && velRadPerSec < 0.0f);
        if (afundando) t -= e.wallDampingNmPerRadPerSec * velRadPerSec;
    }
    return t;
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

    // ⚠️ ALÉM DO FIM DO CURSO, O JOGO NÃO AJUDA A FURAR A PAREDE.
    //
    // Este é o motivo estrutural de o batente ser vencível, e nenhuma rigidez conserta sozinha: a
    // parede e a força do jogo eram somadas e depois cortadas pelo MESMO teto. Numa curva pesada o
    // jogo já pede o teto inteiro, então a parede entrava com o que sobrava — nada — e o volante
    // passava. Quanto mais forte a curva, mais fraco o fim de curso: exatamente ao contrário.
    //
    // A parcela que empurra para FORA DESVANECE ao longo da rampa: inteira na entrada dela, zero no
    // fim do curso. A que empurra para DENTRO passa sempre — voltar nunca é bloqueado.
    //
    // ⚠️ DESVANECER, e não cortar no fim do curso. Cortar seco foi a primeira versão, e a simulação
    // da chegada reprovou: o jogo desliga ao cruzar a linha e religa ao voltar um décimo de grau,
    // 917 inversões em 4 s — o volante trepidaria na parede. Com a atenuação contínua caem para ~20,
    // que é o assentamento, e não vibração. Uma descontinuidade de 15 Nm dentro de uma malha de
    // 1 kHz não tem como não virar chatter.
    //
    // As implementações de referência resolvem o mesmo problema reservando uma fatia fixa do teto
    // para o batente; atenuar só a componente que fura chega no mesmo lugar sem cobrar força do jogo
    // no resto do curso, que é onde se dirige.
    if (ec.wallRad > 0.0f) {
        const float zona = ec.wallRad - ec.rangeRad;
        const float absPos = positionRad < 0.0f ? -positionRad : positionRad;
        // Sem zona de rampa (soft-stop range = 0) não há o que desvanecer: vira liga-desliga no fim
        // do curso mesmo. Dividir por zero aqui produziria inf/NaN e levaria o NaN até o motor.
        float fator = (zona > 1e-4f) ? (ec.wallRad - absPos) / zona : (absPos >= ec.wallRad ? 0.0f : 1.0f);
        fator = clampf(fator, 0.0f, 1.0f);
        if ((positionRad > 0.0f && t > 0.0f) || (positionRad < 0.0f && t < 0.0f)) t *= fator;
    }

    t += endstopTorque(positionRad, ec);                                // fim de curso (rampa + muro)
    t += endstopDamping(positionRad, velRadPerSec, ec);                 // + amortecimento (não quica)
    return t;                                                           // SEM teto (para medir clipping)
}

inline float computeTorque(float hostForce, float positionRad, float velRadPerSec,
                           const ForceConfig& fc, const EffectConfig& ef, const EndstopConfig& ec) {
    return clampf(computeTorqueRaw(hostForce, positionRad, velRadPerSec, fc, ef, ec),
                  -fc.torqueLimitNm, fc.torqueLimitNm);                 // teto duro por último
}

}  // namespace drivelab
