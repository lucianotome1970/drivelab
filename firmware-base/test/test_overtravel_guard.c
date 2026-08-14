// ============================================================================
//  DriveLab
//  test_overtravel_guard.c — A guarda de curso excedido: freia, e só desarma
//  quando o freio não é obedecido.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include <stdio.h>
#include "../inc/overtravel_guard.h"

static int falhas = 0;

static void ok(const char* nome, int cond) {
    printf("  %s     %s\n", cond ? "ok" : "FALHOU", nome);
    if (!cond) falhas++;
}

// Curso de 900°: meia-volta de cada lado = 450° = 7,854 rad.
static const float kDor = 7.853982f;

int main(void) {
    printf("== test_overtravel_guard.c\n");

    // ---- inerte no uso normal -------------------------------------------------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();

        ok("centro nao dispara",
           overtravel_update(&s, &c, 0.0f, kDor, 0.0f, 1) == OT_ACT_NORMAL);
        ok("batente nao dispara",
           overtravel_update(&s, &c, kDor, kDor, 0.0f, 1) == OT_ACT_NORMAL);
        // 44° alem do fim do curso: ainda dentro da margem de 45°.
        ok("44 graus alem ainda nao dispara",
           overtravel_update(&s, &c, kDor + 0.7679f, kDor, 0.0f, 1) == OT_ACT_NORMAL);
        // Giro rapido e legitimo, dentro do curso: nao e assunto desta guarda.
        ok("giro rapido dentro do curso nao dispara",
           overtravel_update(&s, &c, kDor * 0.9f, kDor, 30.0f, 1) == OT_ACT_NORMAL);
    }

    // ---- dispara alem da margem, e freia ANTES de desarmar ---------------------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();

        const float fora = kDor + 0.85f;   // ~49° alem
        ok("46 graus alem dispara",
           overtravel_update(&s, &c, fora, kDor, 10.0f, 1) == OT_ACT_BRAKE);
        ok("a primeira acao e FREIAR, nao desarmar", s.state == OT_ST_BRAKING);
        ok("guarda a prova da posicao", s.last_pos_mrad > 0);
        ok("guarda a prova da velocidade", s.last_vel_mrad_s == 10000);

        // Ainda girando: segue freando enquanto houver janela.
        ok("segue freando", overtravel_update(&s, &c, fora, kDor, 9.0f, 1) == OT_ACT_BRAKE);
    }

    // ---- o freio OBEDECE: desarma, e no modo LOCK trava -----------------------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();   // mode = LOCK
        const float fora = kDor + 0.85f;

        overtravel_update(&s, &c, fora, kDor, 10.0f, 1);          // dispara
        ok("parou => desarma",
           overtravel_update(&s, &c, fora, kDor, 0.1f, 1) == OT_ACT_DISARM);
        ok("modo LOCK trava", s.state == OT_ST_LOCKED);
        ok("registra que fomos nos", s.disarmed_by_us == 1);
        // Mesmo voltando ao centro e parado, nao volta sozinho.
        ok("travado nao re-arma nem no centro",
           overtravel_update(&s, &c, 0.0f, kDor, 0.0f, 1) == OT_ACT_HOLD);
    }

    // ---- o freio NAO e obedecido: trava mesmo no modo re-armar -----------------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        c.mode = (uint8_t)OT_MODE_REARM;              // usuario pediu re-arme...
        const float fora = kDor + 0.85f;

        overtravel_update(&s, &c, fora, kDor, 40.0f, 1);          // dispara
        OvertravelAction a = OT_ACT_BRAKE;
        for (int i = 0; i < 200 && a == OT_ACT_BRAKE; ++i)
            a = overtravel_update(&s, &c, fora + i * 0.01f, kDor, 40.0f, 1);   // nao desacelera

        ok("freio ignorado => desarma", a == OT_ACT_DISARM);
        ok("...e TRAVA, ignorando o modo re-armar", s.state == OT_ST_LOCKED);
    }

    // ---- modo re-armar: volta so DENTRO do curso e PARADO ----------------------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        c.mode = (uint8_t)OT_MODE_REARM;
        const float fora = kDor + 0.85f;

        overtravel_update(&s, &c, fora, kDor, 10.0f, 1);                       // dispara
        overtravel_update(&s, &c, fora, kDor, 0.1f, 1);                        // freia e desarma
        ok("modo re-armar fica esperando", s.state == OT_ST_OUT);

        ok("ainda fora do curso: nao re-arma",
           overtravel_update(&s, &c, fora, kDor, 0.0f, 1) == OT_ACT_HOLD);
        ok("dentro do curso mas GIRANDO: nao re-arma",
           overtravel_update(&s, &c, 0.0f, kDor, 20.0f, 1) == OT_ACT_HOLD);
        ok("dentro e parado: re-arma",
           overtravel_update(&s, &c, 0.0f, kDor, 0.0f, 1) == OT_ACT_NORMAL);
        ok("volta ao normal", s.state == OT_ST_NORMAL);
        ok("contou o disparo", s.trips == 1);
    }

    // ---- histerese: recuperar exige voltar ao curso, nao so sair da margem -----------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        c.mode = (uint8_t)OT_MODE_REARM;

        overtravel_update(&s, &c, kDor + 0.85f, kDor, 10.0f, 1);
        overtravel_update(&s, &c, kDor + 0.85f, kDor, 0.1f, 1);   // desarmou

        // Voltou para 30° alem do fim: ja saiu da margem de 45°, mas AINDA nao voltou ao curso.
        ok("sair da margem nao basta",
           overtravel_update(&s, &c, kDor + 0.52f, kDor, 0.0f, 1) == OT_ACT_HOLD);
        ok("exatamente no fim do curso ja conta como dentro",
           overtravel_update(&s, &c, kDor, kDor, 0.0f, 1) == OT_ACT_NORMAL);
    }

    // ---- teto de re-armes: tres disparos nao sao mais "bati no muro" -----------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        c.mode = (uint8_t)OT_MODE_REARM;
        c.max_trips = 3;
        const float fora = kDor + 0.85f;

        for (int i = 0; i < 3; ++i) {
            overtravel_update(&s, &c, fora, kDor, 10.0f, 1);   // dispara
            overtravel_update(&s, &c, fora, kDor, 0.1f, 1);    // freia, desarma
            overtravel_update(&s, &c, 0.0f, kDor, 0.0f, 1);    // volta ao curso, parado
        }
        ok("no terceiro disparo trava", s.state == OT_ST_LOCKED);
        ok("e nao volta mais",
           overtravel_update(&s, &c, 0.0f, kDor, 0.0f, 1) == OT_ACT_HOLD);
    }

    // ---- simetria: o lado negativo vale igual ---------------------------------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        ok("dispara tambem girando para o outro lado",
           overtravel_update(&s, &c, -(kDor + 0.85f), kDor, -10.0f, 1) == OT_ACT_BRAKE);
    }

    // ---- curso pequeno (formula, 360°): a margem nao pode engolir o curso ------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        const float dorF1 = 3.14159f;   // 360° totais => 180° de cada lado

        ok("no batente do formula nao dispara",
           overtravel_update(&s, &c, dorF1, dorF1, 0.0f, 1) == OT_ACT_NORMAL);
        ok("46 graus alem do batente do formula dispara",
           overtravel_update(&s, &c, dorF1 + 0.81f, dorF1, 5.0f, 1) == OT_ACT_BRAKE);
    }

    printf(falhas ? "\nFALHAS: %d\n" : "\nTUDO OK\n", falhas);
    return falhas ? 1 : 0;
}
