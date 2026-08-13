// ============================================================================
//  DriveLab
//  center_drive.h — Leva o volante ATE o centro no boot, com a base movendo o
//  aro. Logica PURA: roda igual no firmware e num alvo de teste no PC.
//
//  ── O QUE ISTO E, E O QUE NAO E ────────────────────────────────────────────
//  O center_recovery.h descobre ONDE o centro esta. Este arquivo faz o volante
//  IR ate la. Sao dois problemas separados, e este e o unico dos dois que move
//  o hardware sozinho.
//
//  ── POR QUE ISTO E DELICADO ────────────────────────────────────────────────
//  Isto e a base aplicando torque no instante em que liga, e ninguem espera que
//  um volante se mexa sozinho ao energizar. Pode haver uma mao no aro. Por isso
//  o controle daqui NAO e "va para o centro custe o que custar":
//
//    · o torque tem TETO PROPRIO, muito abaixo do FFB — o suficiente para
//      mover um aro livre, nao para vencer um braco;
//    · se alguem segura, ele DESISTE em vez de empurrar sem parar. Um motor que
//      insiste contra uma mao e o mesmo motor que esquenta e assusta;
//    · ha um prazo maximo. Sem ele, um volante travado por um cabo enroscado
//      viraria torque permanente contra o obstaculo;
//    · so termina quando ASSENTA (perto do centro E parado). Terminar ao
//      "passar" pelo centro deixaria o aro girando por inercia.
//
//  ── POR QUE MOLA+AMORTECIMENTO, E NAO CONTROLE DE POSICAO ──────────────────
//  O ODrive tem modo de posicao, mas entrar nele exige ganhos que nao ajustamos
//  para esta finalidade, e sair dele de volta para torque no meio do boot e mais
//  uma transicao para dar errado. Uma mola amortecida em modo de TORQUE — o modo
//  em que ja estamos — chega ao mesmo lugar com o comportamento que queremos:
//  aproxima-se devagar, assenta sem quicar, e cede se encontrar resistencia.
//
//  ── CONVENCAO DE SINAL ─────────────────────────────────────────────────────
//  Torque positivo aumenta pos_turns, igual ao endstop do ffb_math.h. A mola
//  daqui e -k*pos, entao ela sempre puxa para zero.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#ifndef DRIVELAB_CENTER_DRIVE_H
#define DRIVELAB_CENTER_DRIVE_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

enum {
    CENTER_DRIVE_IDLE    = 0,  // nao pedido, ou ja encerrado sem ter comecado
    CENTER_DRIVE_MOVING  = 1,  // aplicando torque em direcao ao centro
    CENTER_DRIVE_DONE    = 2,  // assentou no centro
    CENTER_DRIVE_HELD    = 3,  // alguem esta segurando (ou travou): desistimos
    CENTER_DRIVE_TIMEOUT = 4   // passou do prazo sem assentar
};

// ── Numeros do comportamento ───────────────────────────────────────────────
// Teto de torque. 1,5 Nm num fundo de escala de 15 e 10%: move um aro solto com
// folga, e e facil de segurar com uma mao. NAO subir isto para "ficar mais
// rapido" — a lentidao aqui e a caracteristica de seguranca, nao um efeito
// colateral.
#define CENTER_DRIVE_MAX_TORQUE_NM   1.5f

// Rigidez da mola, Nm por volta de erro. Com o teto acima, satura a ~0,25 volta
// (90°) — ou seja, na maior parte do curso ele vai no teto, e so afrouxa perto
// do centro, que e onde a suavidade importa.
#define CENTER_DRIVE_STIFFNESS       6.0f

// Amortecimento, Nm por volta/s. Sem ele a mola faz o aro passar do centro e
// voltar, varias vezes — a pessoa ve o volante "procurando" o centro, que
// parece defeito mesmo quando termina certo.
#define CENTER_DRIVE_DAMPING         1.2f

// Assentou: perto o bastante (0,004 volta = 1,4°) e parado o bastante, por um
// tempo minimo. O tempo existe porque passar pelo centro a caminho do outro
// lado satisfaz "perto" por um instante.
#define CENTER_DRIVE_SETTLE_TURNS    0.004f
#define CENTER_DRIVE_SETTLE_VEL      0.02f
#define CENTER_DRIVE_SETTLE_MS       200u

// Alguem segurando: torque no teto e o volante praticamente parado, longe do
// centro, por este tempo. Nao e falha — e a resposta certa a uma mao no aro.
#define CENTER_DRIVE_HELD_VEL        0.01f
#define CENTER_DRIVE_HELD_MS         1200u

// Prazo total. Depois disto para, tenha chegado ou nao.
#define CENTER_DRIVE_TIMEOUT_MS      6000u

typedef struct {
    uint8_t  state;
    uint32_t elapsed_ms;
    uint32_t settled_ms;   // quanto tempo seguido dentro da janela de assentamento
    uint32_t stuck_ms;     // quanto tempo seguido parado longe do centro
    float    torque_nm;    // o que aplicar AGORA (0 fora do estado MOVING)
} CenterDrive;

// Pede o movimento. So chame com a referencia de centro CONFIAVEL: levar o
// volante a um centro errado e pior que nao mover — o batente vai junto.
static inline void center_drive_start(CenterDrive* d) {
    d->state      = CENTER_DRIVE_MOVING;
    d->elapsed_ms = 0u;
    d->settled_ms = 0u;
    d->stuck_ms   = 0u;
    d->torque_nm  = 0.0f;
}

static inline void center_drive_abort(CenterDrive* d) {
    d->state     = CENTER_DRIVE_IDLE;
    d->torque_nm = 0.0f;
}

static inline int center_drive_is_moving(const CenterDrive* d) {
    return d->state == CENTER_DRIVE_MOVING;
}

// Um passo do controle. pos_turns e vel_turns_s vem do centro JA reconstruido
// (wheel_center_pos_turns), nao da contagem crua. dt_ms e o periodo do laco.
//
// Devolve o torque a aplicar. Fora do estado MOVING devolve SEMPRE zero — nao
// existe caminho de saida deste modulo que deixe torque pendurado.
static inline float center_drive_step(CenterDrive* d, float pos_turns,
                                      float vel_turns_s, uint32_t dt_ms) {
    if (d->state != CENTER_DRIVE_MOVING) {
        d->torque_nm = 0.0f;
        return 0.0f;
    }

    d->elapsed_ms += dt_ms;

    const float err = (pos_turns >= 0.0f) ? pos_turns : -pos_turns;
    const float spd = (vel_turns_s >= 0.0f) ? vel_turns_s : -vel_turns_s;

    // ASSENTOU? Perto e parado, por tempo suficiente. As duas condicoes juntas:
    // "perto" sozinho aceita o aro passando batido pelo centro.
    if (err <= CENTER_DRIVE_SETTLE_TURNS && spd <= CENTER_DRIVE_SETTLE_VEL) {
        d->settled_ms += dt_ms;
        if (d->settled_ms >= CENTER_DRIVE_SETTLE_MS) {
            d->state     = CENTER_DRIVE_DONE;
            d->torque_nm = 0.0f;
            return 0.0f;
        }
    } else {
        d->settled_ms = 0u;
    }

    // SEGURANDO? Longe do centro e sem andar, com a mola ja puxando. Desistir
    // aqui e o comportamento correto: continuar seria empurrar contra uma mao.
    if (err > CENTER_DRIVE_SETTLE_TURNS && spd <= CENTER_DRIVE_HELD_VEL) {
        d->stuck_ms += dt_ms;
        if (d->stuck_ms >= CENTER_DRIVE_HELD_MS) {
            d->state     = CENTER_DRIVE_HELD;
            d->torque_nm = 0.0f;
            return 0.0f;
        }
    } else {
        d->stuck_ms = 0u;
    }

    // PRAZO. Um volante preso por um cabo nunca assenta nem "para" o bastante
    // para contar como segurado; sem prazo isto viraria torque permanente.
    if (d->elapsed_ms >= CENTER_DRIVE_TIMEOUT_MS) {
        d->state     = CENTER_DRIVE_TIMEOUT;
        d->torque_nm = 0.0f;
        return 0.0f;
    }

    // Mola para o centro + amortecimento, com teto proprio.
    float t = -CENTER_DRIVE_STIFFNESS * pos_turns
              - CENTER_DRIVE_DAMPING  * vel_turns_s;
    if (t >  CENTER_DRIVE_MAX_TORQUE_NM) t =  CENTER_DRIVE_MAX_TORQUE_NM;
    if (t < -CENTER_DRIVE_MAX_TORQUE_NM) t = -CENTER_DRIVE_MAX_TORQUE_NM;

    d->torque_nm = t;
    return t;
}

#ifdef __cplusplus
}
#endif

#endif // DRIVELAB_CENTER_DRIVE_H
