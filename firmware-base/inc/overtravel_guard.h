// ============================================================================
//  DriveLab
//  overtravel_guard.h — O volante não pode estar onde ele não tem direito de
//  estar. Guarda por POSIÇÃO, complementar às de velocidade e de corrente.
//
//  O ARGUMENTO: o curso é conhecido (o DOR configurado), e o batente por
//  software começa alguns graus antes do fim. Se o volante aparece MUITO além
//  do fim do curso, só há duas explicações, e as duas são falha: o batente foi
//  vencido, ou a FOC está girando o motor sozinha. Não existe terceira — nenhum
//  piloto, com nenhuma força de braço, leva o volante para lá com o firmware
//  funcionando. É um teste binário, sem zona cinzenta.
//
//  POR QUE AS OUTRAS DUAS GUARDAS NÃO COBREM ISTO:
//    · a de sobrevelocidade exige 5 voltas/s por 150 ms — 0,75 volta de curso
//      percorrido antes de agir, e é CEGA para o disparo lento (um offset ruim
//      que produza torque parasita fraco gira meia volta por segundo para
//      sempre sem nunca cruzar o limiar);
//    · a de coerência do ângulo elétrico exige corrente alta E torque comandado
//      baixo E velocidade baixa — o retrato do motor travado. Um motor que gira
//      solto não se parece com isso.
//  Nenhuma pergunta "onde o volante está?", que é o dado mais simples e mais
//  verdadeiro que temos.
//
//  A AÇÃO, EM DUAS ETAPAS, com auto-diagnóstico embutido:
//    1. FREIO CONTROLADO: zera a força do jogo e aplica só amortecimento
//       proporcional à velocidade. Se a causa era batente vencido ou força
//       congelada, o volante desacelera e para — sem parede, sem tranco.
//    2. SE A VELOCIDADE NÃO CAIR nessa janela, desarma. E repare no que essa
//       condição significa: "mandei frear e o eixo não obedeceu" é a assinatura
//       limpa de calibração ruim — o torque comandado não está saindo no ângulo
//       certo, que é justamente a falha que o freio não consegue tratar.
//
//  RE-ARME (escolha do usuário, padrão = travar): re-armar sozinho exige DUAS
//  condições, não uma — o volante de volta DENTRO do curso E parado. Só a
//  primeira geraria um laço infinito: re-arma ainda fora, dispara no mesmo tick,
//  repete. E há teto de re-armes: três disparos seguidos não são mais "bati no
//  muro", são defeito, e aí trava de qualquer forma.
//
//  Lógica PURA (sem STM32, sem motor): roda igual no firmware e no PC.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#ifndef DRIVELAB_OVERTRAVEL_GUARD_H
#define DRIVELAB_OVERTRAVEL_GUARD_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/// O que o chamador deve fazer NESTE tick.
typedef enum {
    OT_ACT_NORMAL = 0,   ///< nada a ver aqui: aplica o torque do pipeline
    OT_ACT_BRAKE  = 1,   ///< aplica SÓ amortecimento (o chamador calcula a partir da velocidade)
    OT_ACT_DISARM = 2,   ///< torque zero + pedir IDLE
    OT_ACT_HOLD   = 3    ///< já desarmado; segura assim (esperando voltar ao curso, ou travado)
} OvertravelAction;

typedef enum {
    OT_MODE_LOCK   = 0,  ///< disparou → trava; só volta com reinício (PADRÃO)
    OT_MODE_REARM  = 1   ///< disparou → re-arma quando voltar ao curso e parar
} OvertravelMode;

typedef struct {
    float    margin_rad;      ///< quanto além do fim do curso ainda é tolerado
    uint16_t brake_ms;        ///< janela do freio controlado antes de desistir
    float    stopped_rad_s;   ///< abaixo disto o volante conta como parado
    uint8_t  mode;            ///< OvertravelMode
    uint8_t  max_trips;       ///< re-armes permitidos antes de travar de vez
} OvertravelCfg;

typedef struct {
    uint8_t  state;           ///< interno: 0=normal 1=freando 2=fora-desarmado 3=travado
    uint16_t brake_ticks;     ///< ms dentro do freio controlado
    float    vel_at_trip;     ///< velocidade no instante do disparo — a referência do "caiu?"
    uint8_t  trips;           ///< quantas vezes já disparou neste boot
    // provas, para SWD e telemetria (só leitura)
    int32_t  last_pos_mrad;   ///< posição no disparo, em milirradianos
    int32_t  last_vel_mrad_s; ///< velocidade no disparo
    uint8_t  disarmed_by_us;  ///< 1 = fomos nós que desarmamos (não outra guarda)
} OvertravelState;

enum { OT_ST_NORMAL = 0, OT_ST_BRAKING = 1, OT_ST_OUT = 2, OT_ST_LOCKED = 3 };

static inline void overtravel_init(OvertravelState* s) {
    s->state = OT_ST_NORMAL;
    s->brake_ticks = 0;
    s->vel_at_trip = 0.0f;
    s->trips = 0;
    s->last_pos_mrad = 0;
    s->last_vel_mrad_s = 0;
    s->disarmed_by_us = 0;
}

static inline float ot_abs(float v) { return v < 0.0f ? -v : v; }

/// Um tick. `pos_rad` é a posição A PARTIR DO CENTRO (o MESMO zero do batente — se forem zeros
/// diferentes, a guarda mede um curso que não é o que a parede usa), `dor_half_rad` é o fim do
/// curso (DOR/2) e `vel_rad_s` a velocidade angular. Chamar a 1 kHz; `dt_ms` existe para o teste
/// poder andar mais rápido que tempo real.
///
/// HISTERESE: dispara em `dor_half + margem` e só se recupera em `dor_half`. Sem essa diferença o
/// volante pousado exatamente no limiar entraria e sairia de disparo a cada tick.
static inline OvertravelAction overtravel_update(OvertravelState* s, const OvertravelCfg* c,
                                                 float pos_rad, float dor_half_rad,
                                                 float vel_rad_s, uint16_t dt_ms) {
    const int   fora    = ot_abs(pos_rad) > (dor_half_rad + c->margin_rad);
    const int   dentro  = ot_abs(pos_rad) <= dor_half_rad;
    const int   parado  = ot_abs(vel_rad_s) <= c->stopped_rad_s;

    switch (s->state) {

    case OT_ST_NORMAL:
        if (!fora) return OT_ACT_NORMAL;
        // Guarda a prova ANTES de agir: depois de desarmar, a posição e a velocidade do instante
        // do disparo já não existem em lugar nenhum.
        s->last_pos_mrad   = (int32_t)(pos_rad   * 1000.0f);
        s->last_vel_mrad_s = (int32_t)(vel_rad_s * 1000.0f);
        s->vel_at_trip     = ot_abs(vel_rad_s);
        s->brake_ticks     = 0;
        s->state           = OT_ST_BRAKING;
        return OT_ACT_BRAKE;

    case OT_ST_BRAKING:
        if (parado) {
            // O freio OBEDECEU. Desarma de todo jeito — não sabemos por que o volante foi parar
            // lá, e seguir aplicando força num lugar onde ele não deveria estar é apostar.
            s->disarmed_by_us = 1;
            s->state = (c->mode == OT_MODE_REARM) ? OT_ST_OUT : OT_ST_LOCKED;
            return OT_ACT_DISARM;
        }
        s->brake_ticks = (uint16_t)(s->brake_ticks + dt_ms);
        if (s->brake_ticks >= c->brake_ms) {
            // Mandamos frear e o eixo NÃO obedeceu. Isto não é batente vencido: é o torque não
            // saindo no ângulo certo — calibração ruim. Trava SEMPRE, ignorando o modo escolhido:
            // re-armar sobre uma calibração que não responde é repetir o disparo de propósito.
            s->disarmed_by_us = 1;
            s->state = OT_ST_LOCKED;
            return OT_ACT_DISARM;
        }
        return OT_ACT_BRAKE;

    case OT_ST_OUT:
        // Desarmado, esperando a pessoa trazer o volante de volta. DUAS condições: dentro do curso
        // E parado. Só a primeira re-armaria com o aro ainda em movimento.
        if (dentro && parado) {
            s->trips = (uint8_t)(s->trips + 1);
            if (s->trips >= c->max_trips) {
                // Três disparos seguidos não são mais "bati no muro" — são defeito.
                s->state = OT_ST_LOCKED;
                return OT_ACT_HOLD;
            }
            s->state = OT_ST_NORMAL;
            return OT_ACT_NORMAL;
        }
        return OT_ACT_HOLD;

    case OT_ST_LOCKED:
    default:
        return OT_ACT_HOLD;
    }
}

/// Padrões: 45° de margem além do fim do curso, 150 ms de freio, e travar por default.
static inline OvertravelCfg overtravel_default_cfg(void) {
    OvertravelCfg c;
    c.margin_rad    = 0.7853982f;   // 45°
    c.brake_ms      = 150u;
    c.stopped_rad_s = 0.5f;         // ~4,8 RPM — parado para efeito prático
    c.mode          = (uint8_t)OT_MODE_LOCK;
    c.max_trips     = 3u;
    return c;
}

#ifdef __cplusplus
}
#endif

#endif  // DRIVELAB_OVERTRAVEL_GUARD_H
