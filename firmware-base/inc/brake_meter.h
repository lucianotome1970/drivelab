// ============================================================================
//  DriveLab
//  brake_meter.h — Medidor do brake chopper: energia dissipada, numero de
//  acionamentos e pico de potencia. Logica PURA (sem STM32, sem driver): roda
//  igual no firmware e num alvo de teste no PC.
//
//  PARA QUE SERVE: a regeneracao de um volante dura milissegundos (medimos
//  3,3 W de media no zig-zag real), entao o resistor de freio quase nunca
//  esquenta. Para quem monta, "frio" parece "quebrado". Estes contadores sao a
//  evidencia de que ele trabalhou — e zero acionamentos depois de uma sessao
//  inteira e diagnostico: resistor desconectado ou rampa mal dimensionada.
//
//  POR QUE A ENERGIA E INTEIRA: acumular joules em float PARA de contar. A
//  ~3 W de media cada incremento a 8 kHz e ~0,0004 J; passando de ~4000 J esse
//  incremento fica abaixo do epsilon do float32 e e descartado — o numero
//  congelaria no meio da sessao e pareceria que o chopper morreu. Aqui o
//  inteiro guarda os millijoules e o float guarda so a fracao do millijoule
//  corrente, entao ele nunca cresce e nunca perde incremento.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_BRAKE_METER_H
#define DRIVELAB_BRAKE_METER_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    uint32_t energy_mj;    // energia dissipada acumulada, em millijoules
    uint32_t activations;  // transicoes de nao-conduzindo para conduzindo
    uint16_t peak_dw;      // pico de potencia, em decimos de watt
    float    frac_mj;      // fracao do millijoule corrente (nunca cresce)
    uint8_t  was_on;       // estado anterior, para detectar a transicao
} BrakeMeter;

static inline void brake_meter_init(BrakeMeter* m) {
    m->energy_mj   = 0u;
    m->activations = 0u;
    m->peak_dw     = 0u;
    m->frac_mj     = 0.0f;
    m->was_on      = 0u;
}

// duty: 0..1 do chopper. vbus: volts. resistance: ohms. dt: segundos.
// Chamada a cada ciclo de controle. So MEDE — nao altera nada do acionamento.
static inline void brake_meter_update(BrakeMeter* m, float duty, float vbus,
                                      float resistance, float dt) {
    const uint8_t on = (duty > 0.0f) ? 1u : 0u;

    // Conta o EVENTO de frenagem, nao o ciclo de PWM: so a transicao off->on.
    if (on && !m->was_on) m->activations++;
    m->was_on = on;

    if (!on || resistance <= 0.0f) return;

    const float p_w = duty * vbus * vbus / resistance;   // watts instantaneos

    const float dw = p_w * 10.0f;                        // decimos de watt
    if (dw > 65535.0f)                m->peak_dw = 65535u;
    else if (dw > (float)m->peak_dw)  m->peak_dw = (uint16_t)dw;

    m->frac_mj += p_w * dt * 1000.0f;        // energia deste ciclo, em mJ
    if (m->frac_mj >= 1.0f) {                // migra a parte inteira e recomeca
        const float inteiro = (float)(int32_t)m->frac_mj;
        m->energy_mj += (uint32_t)inteiro;
        m->frac_mj   -= inteiro;
    }
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_BRAKE_METER_H
