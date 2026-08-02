/*
 * foc_offset_cal.h — logica pura da calibracao de offset FOC (media circular + corte).
 *
 * Puro e host-testavel. Acumula a media circular do offset eletrico para as duas
 * hipoteses de direcao (CW/CCW) a partir de uma varredura de pole_mech vs field, e
 * escolhe a direcao cujo phasor medio e mais "apertado" (maior raio = ajuste mais
 * coerente sob cogging/ruido). Tambem oferece o predicado de corte por corrente.
 *
 * Autor: Luciano Tomé <lucianotome1970@gmail.com>
 * Licença: MIT
 */
#pragma once
#include <math.h>
#include <cstdint>

namespace drivelab {

// Acumula a média circular do offset elétrico p/ as duas hipóteses de direção.
// aP = pole_pairs*θmec - θcampo (CW) ; aM = -pole_pairs*θmec - θcampo (CCW).
// O chamador passa poleMechAngle = pole_pairs*θmecânico (elétrico) e fieldAngle.
struct OffsetAccumulator {
    double sinP = 0, cosP = 0, sinM = 0, cosM = 0;
    int n = 0;
    void add(float poleMechAngle, float fieldAngle) {
        const float aP = poleMechAngle - fieldAngle;
        const float aM = -poleMechAngle - fieldAngle;
        sinP += sinf(aP); cosP += cosf(aP);
        sinM += sinf(aM); cosM += cosf(aM);
        ++n;
    }
    int count() const { return n; }
};

struct OffsetResult {
    float zeroElectric;   // média circular do offset (rad elétrico) da direção escolhida
    int   direction;      // +1 = CW, -1 = CCW
    float fit;            // raio do phasor médio (0..1); 1 = perfeito, baixo = escorregou/ruído
    bool  ok;             // n>0 && fit >= minFit
};

// Escolhe a direção com o phasor mais "apertado" (maior raio = ajuste mais coerente).
// physicalDir: direção FÍSICA medida no scan (sign do dFwd = quanto o encoder REALMENTE andou enquanto o campo
// avançava +). +1 => usa o phasor P (+1), -1 => phasor M (-1). 0 (default) = legado: escolhe pelo fit (tightness).
// >>> Escolher a direção pela FÍSICA (não pelo fit) é a correção do RUNAWAY: o fit podia apontar a direção errada
// (phasor do lado errado mais "apertado" no ruído) → a mola empurrava pro lado errado → motor disparava. O dFwd é
// o movimento REAL do rotor, não erra de sentido. Mapeamento: campo sobe (aP=poleMech-field const p/ +1) → rotor
// segue → encoder sobe → dFwd>0 => +1 (P); encoder desce => -1 (M). VALIDAR na bancada (motor frio) — se a 1ª cal
// der runaway no VERIFY, o mapeamento está invertido (trocar o sinal); o VERIFY + corte de runaway protegem.
inline OffsetResult computeOffset(const OffsetAccumulator& a, float minFit, int physicalDir = 0) {
    OffsetResult r{0.0f, +1, 0.0f, false};
    if (a.n <= 0) return r;
    const float invN = 1.0f / (float)a.n;
    const float rP = (float)(hypot(a.sinP, a.cosP) * invN);
    const float rM = (float)(hypot(a.sinM, a.cosM) * invN);
    bool useP;
    if (physicalDir > 0)      useP = true;           // direção pela FÍSICA do scan (confiável)
    else if (physicalDir < 0) useP = false;
    else                      useP = (rP >= rM);     // fallback legado: pelo fit
    if (useP) {
        r.direction = +1; r.fit = rP;
        r.zeroElectric = (float)atan2(a.sinP, a.cosP);
    } else {
        r.direction = -1; r.fit = rM;
        r.zeroElectric = (float)atan2(a.sinM, a.cosM);
    }
    r.ok = (a.n > 0) && (r.fit >= minFit);
    return r;
}

// Predicado de corte por corrente (magnitude da corrente q vs teto).
inline bool currentOverLimit(float iq, float iLimit) {
    return fabsf(iq) > iLimit;
}

} // namespace drivelab
