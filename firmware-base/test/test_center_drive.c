// ============================================================================
//  DriveLab
//  test_center_drive.c — Testes de host do movimento ate o centro no boot.
//
//  Isto e a base movendo o aro sozinha ao energizar, possivelmente com uma mao
//  nele. Entao o que se prova aqui nao e "chega ao centro" — e que ele DESISTE
//  quando encontra resistencia, que tem prazo, que nunca passa do proprio teto
//  de torque, e que nenhum caminho de saida deixa torque aplicado.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "../inc/center_drive.h"
#include <stdio.h>

static int falhas = 0;

static void check(int cond, const char* nome) {
    if (cond) { printf("  ok     %s\n", nome); }
    else      { printf("  FALHOU %s\n", nome); falhas++; }
}

// Volante simulado: inercia + atrito, movido pelo torque que o controle manda.
// Numeros na ordem de grandeza de um aro de hoverboard (nao precisam ser exatos:
// o que se testa e o COMPORTAMENTO do controle, nao a fidelidade do modelo).
typedef struct { float pos, vel; } Aro;

static void aro_passo(Aro* a, float torque_nm, float dt) {
    const float inercia = 0.03f;    // kg·m² aproximado
    const float atrito  = 0.15f;
    const float acel = (torque_nm - atrito * a->vel) / inercia;
    a->vel += acel * dt;
    a->pos += a->vel * dt;
}

int main(void) {
    const float dt = 0.001f;
    const uint32_t dt_ms = 1u;

    // 1) parado antes de pedir: NADA de torque
    {
        CenterDrive d = {0};
        d.state = CENTER_DRIVE_IDLE;
        check(center_drive_step(&d, 0.9f, 0.0f, dt_ms) == 0.0f,
              "sem pedir o movimento nao ha torque");
    }

    // 2) leva ao centro e ASSENTA (nao so passa por ele)
    {
        CenterDrive d; center_drive_start(&d);
        Aro a = { 0.6f, 0.0f };                     // ~216° fora do centro
        for (int i = 0; i < 20000 && center_drive_is_moving(&d); i++) {
            float t = center_drive_step(&d, a.pos, a.vel, dt_ms);
            aro_passo(&a, t, dt);
        }
        check(d.state == CENTER_DRIVE_DONE, "chega ao centro e conclui");
        float err = a.pos >= 0 ? a.pos : -a.pos;
        check(err <= CENTER_DRIVE_SETTLE_TURNS * 2.0f, "para PERTO do centro");
        float spd = a.vel >= 0 ? a.vel : -a.vel;
        check(spd <= CENTER_DRIVE_SETTLE_VEL * 2.0f, "para PARADO, nao de passagem");
    }

    // 3) funciona vindo do outro lado (sinal)
    {
        CenterDrive d; center_drive_start(&d);
        Aro a = { -0.6f, 0.0f };
        for (int i = 0; i < 20000 && center_drive_is_moving(&d); i++) {
            float t = center_drive_step(&d, a.pos, a.vel, dt_ms);
            aro_passo(&a, t, dt);
        }
        check(d.state == CENTER_DRIVE_DONE, "chega ao centro vindo do lado negativo");
    }

    // 4) NUNCA passa do teto de torque — nem no pior caso (bem longe do centro)
    {
        CenterDrive d; center_drive_start(&d);
        float pico = 0.0f;
        for (int i = 0; i < 500; i++) {
            float t = center_drive_step(&d, 1.4f, -3.0f, dt_ms);   // longe e vindo rapido
            float at = t >= 0 ? t : -t;
            if (at > pico) pico = at;
        }
        check(pico <= CENTER_DRIVE_MAX_TORQUE_NM + 1e-4f,
              "respeita o proprio teto de torque");
    }

    // 5) ALGUEM SEGURANDO: desiste, e desiste ZERANDO o torque.
    //    Este e o teste que mais importa. Um motor que insiste contra uma mao e
    //    o mesmo motor que esquenta, assusta e machuca.
    {
        CenterDrive d; center_drive_start(&d);
        float t = 0.0f;
        for (int i = 0; i < 5000 && center_drive_is_moving(&d); i++)
            t = center_drive_step(&d, 0.5f, 0.0f, dt_ms);   // preso, nao anda
        check(d.state == CENTER_DRIVE_HELD, "desiste quando alguem segura");
        check(t == 0.0f, "ao desistir, zera o torque");
        check(d.elapsed_ms < CENTER_DRIVE_TIMEOUT_MS,
              "desiste ANTES do prazo (nao fica empurrando ate o fim)");
    }

    // 6) PRAZO: volante que se mexe mas nunca assenta nao empurra para sempre
    {
        CenterDrive d; center_drive_start(&d);
        float t = 1.0f;
        // oscila longe do centro: nem assenta, nem conta como segurado
        for (int i = 0; i < 20000 && center_drive_is_moving(&d); i++) {
            float vel = (i % 2) ? 0.5f : -0.5f;
            t = center_drive_step(&d, 0.4f, vel, dt_ms);
        }
        check(d.state == CENTER_DRIVE_TIMEOUT, "tem prazo maximo");
        check(t == 0.0f, "ao estourar o prazo, zera o torque");
    }

    // 7) depois de encerrado (qualquer motivo), continua em zero
    {
        CenterDrive d; center_drive_start(&d);
        for (int i = 0; i < 5000 && center_drive_is_moving(&d); i++)
            center_drive_step(&d, 0.5f, 0.0f, dt_ms);
        check(center_drive_step(&d, 0.9f, 0.0f, dt_ms) == 0.0f,
              "encerrado nunca mais aplica torque");
    }

    // 8) abortar de fora (desconexao, erro, botao) zera na hora
    {
        CenterDrive d; center_drive_start(&d);
        center_drive_step(&d, 0.5f, 0.0f, dt_ms);
        center_drive_abort(&d);
        check(d.torque_nm == 0.0f, "abortar zera o torque imediatamente");
        check(center_drive_step(&d, 0.5f, 0.0f, dt_ms) == 0.0f,
              "abortado nao volta a aplicar torque");
    }

    // 9) ja no centro: conclui sem sacudir o aro
    {
        CenterDrive d; center_drive_start(&d);
        float pico = 0.0f;
        for (int i = 0; i < 5000 && center_drive_is_moving(&d); i++) {
            float t = center_drive_step(&d, 0.0f, 0.0f, dt_ms);
            float at = t >= 0 ? t : -t;
            if (at > pico) pico = at;
        }
        check(d.state == CENTER_DRIVE_DONE, "ja centrado conclui");
        check(pico < 0.05f, "ja centrado nao da tranco");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas);
    else        printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
