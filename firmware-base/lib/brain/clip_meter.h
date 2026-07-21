// ============================================================================
//  DriveLab
//  clip_meter.h — Medidor de clipping do FFB: quanto o torque pedido excede o teto (peak-hold decaído).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once
#include <math.h>
#include <stdint.h>

// Clipping acontece quando a força pedida pelo jogo passa do teto de torque do dispositivo: a saída satura e
// "corta" o topo do efeito (o usuário perde detalhe e sente uma força chapada). Este medidor quantifica isso:
// mantém um nível 0..1 com peak-hold decaído — sobe na hora quando corta e cai suave, para o app mostrar um
// indicador legível (verde/laranja/vermelho) e o usuário baixar o ganho.
//
// Puro e sem dependência de placa: a matemática roda idêntica no host (testes) e no firmware.
class ClipMeter {
public:
    // Excesso instantâneo: 0 se |pedido| <= teto; senão (|pedido|-|teto|)/|pedido| em (0,1).
    // Ex.: pedido = 2× o teto → 0,5 (metade do sinal foi cortada).
    static float instantaneous(float requestedNm, float limitNm) {
        const float r = fabsf(requestedNm);
        const float l = fabsf(limitNm);
        if (l <= 0.0f) return r > 0.0f ? 1.0f : 0.0f;   // sem teto útil → satura tudo
        if (r <= l) return 0.0f;
        return (r - l) / r;
    }

    // Atualiza o peak-hold: decai _decayPerSec por segundo e sobe imediatamente para o excesso instantâneo.
    void update(float requestedNm, float limitNm, float dt) {
        const float inst = instantaneous(requestedNm, limitNm);
        _level -= _decayPerSec * dt;
        if (_level < 0.0f) _level = 0.0f;
        if (inst > _level) _level = inst;
    }

    float level() const { return _level; }                          // 0..1
    uint8_t level255() const { return (uint8_t)(_level * 255.0f + 0.5f); }  // 0..255 p/ telemetria
    void reset() { _level = 0.0f; }
    void setDecayPerSec(float d) { _decayPerSec = d; }

private:
    float _level = 0.0f;
    float _decayPerSec = 1.5f;   // 1 → 0 em ~0,66 s (indicador visível mas não grudento)
};
