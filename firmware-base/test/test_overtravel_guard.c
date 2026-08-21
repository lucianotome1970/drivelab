// ============================================================================
//  DriveLab
//  test_overtravel_guard.c — A guarda de curso excedido: empurra de volta, e só
//  desarma quando o volante FOGE da mola (acelerando e se afastando).
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

// Leva a guarda ate o desarme pelo UNICO caminho que ainda desarma: fuga (rapido E se afastando).
// Existe porque varios testes precisam do estado pos-desarme, e a receita antiga ("dispara, para,
// desarma") deixou de valer -- parar fora do curso agora e segurado, nao desarmado.
static void fugir_ate_desarmar(OvertravelState* s, const OvertravelCfg* c, float fora, float dor) {
    overtravel_update(s, c, fora, dor, 40.0f, 1);
    OvertravelAction a = OT_ACT_RECENTER;
    for (int i = 0; i < 2000 && a == OT_ACT_RECENTER; ++i)
        a = overtravel_update(s, c, fora + i * 0.04f, dor, 40.0f, 1);
}

// Poe a maquina em OT_ST_OUT (desarmado, esperando o volante voltar).
//
// POR QUE DIRETO, E NAO FUGINDO: desde que a fuga passou a TRAVAR em qualquer modo, nenhum caminho
// normal chega mais ao OUT — ele sobrou para o caso de excecao, quando o pedido de desarme nao e
// obedecido. A logica de re-arme continua existindo e continua precisando de teste; o que mudou foi
// como se chega ate ela. Fugir para tentar chegar aqui era o que fazia estes casos falharem, e o
// teto de re-armes passar sem exercitar nada.
static void por_em_out(OvertravelState* s) {
    s->state = (uint8_t)OT_ST_OUT;
    s->disarmed_by_us = 1;
    s->fuga_ticks = 0;
}

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

    // ---- dispara alem da margem, e a acao e EMPURRAR de volta ------------------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();

        const float fora = kDor + 0.85f;   // ~49° alem
        ok("46 graus alem dispara",
           overtravel_update(&s, &c, fora, kDor, 10.0f, 1) == OT_ACT_RECENTER);
        ok("a acao e EMPURRAR DE VOLTA, nao desarmar", s.state == OT_ST_RECENTER);
        ok("guarda a prova da posicao", s.last_pos_mrad > 0);
        ok("guarda a prova da velocidade", s.last_vel_mrad_s == 10000);

        // Ainda girando: segue empurrando, sem relogio nenhum correndo contra ele.
        ok("segue empurrando", overtravel_update(&s, &c, fora, kDor, 9.0f, 1) == OT_ACT_RECENTER);
    }

    // ---- O CASO QUE MUDOU: parar fora do curso NAO desarma mais ---------------------------
    //
    // Este bloco testava o contrario -- "parou => desarma". Era a decisao errada pelo custo: numa
    // corrida, passar do fim do curso e voltar e um susto; ficar sem base e o fim da sessao. E o
    // volante passava do curso por um defeito que nao era desta guarda (a parede era engolida pela
    // forca do jogo), entao ela desarmava punindo o sintoma de outro.
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();   // mode = LOCK
        const float fora = kDor + 0.85f;

        overtravel_update(&s, &c, fora, kDor, 10.0f, 1);          // dispara
        ok("parou fora do curso: SEGURA, nao desarma",
           overtravel_update(&s, &c, fora, kDor, 0.1f, 1) == OT_ACT_RECENTER);
        ok("motor continua armado", s.state == OT_ST_RECENTER);
        ok("ninguem desarmou", s.disarmed_by_us == 0);

        // Segurado la fora por 2 s inteiros: continua empurrando, nao desiste.
        OvertravelAction a = OT_ACT_RECENTER;
        for (int i = 0; i < 2000 && a == OT_ACT_RECENTER; ++i)
            a = overtravel_update(&s, &c, fora, kDor, 0.1f, 1);
        ok("2 s segurado la fora e ainda empurrando", a == OT_ACT_RECENTER);

        // E quando a mola traz de volta: devolve o controle na hora, sem re-arme (nunca desarmou).
        ok("voltou ao curso => devolve o controle",
           overtravel_update(&s, &c, kDor * 0.9f, kDor, -2.0f, 1) == OT_ACT_NORMAL);
        ok("volta ao normal", s.state == OT_ST_NORMAL);
        ok("nao contou re-arme, porque nao houve desarme", s.trips == 0);
        ok("mas o disparo foi registrado", s.disparos == 1);
    }

    // ---- empurrar FIRME com a mao tambem nao desarma ---------------------------------------
    //
    // O cenario que desligava a base: a janela antiga era de 150 ms, e bastava seguir girando o aro
    // contra o batente para ela expirar. Agora velocidade sozinha nao basta -- tem de vir junto de
    // distancia crescente, e por 400 ms.
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        const float fora = kDor + 0.85f;

        overtravel_update(&s, &c, fora, kDor, 5.0f, 1);
        OvertravelAction a = OT_ACT_RECENTER;
        for (int i = 0; i < 1000 && a == OT_ACT_RECENTER; ++i)
            a = overtravel_update(&s, &c, fora, kDor, 5.0f, 1);   // rapido, mas SEM ganhar distancia
        ok("veloz e parado no lugar nao e fuga", a == OT_ACT_RECENTER);
    }

    // ---- afastar-se DEVAGAR (a mao vencendo a mola no braco) tambem nao desarma ------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        const float fora = kDor + 0.85f;

        overtravel_update(&s, &c, fora, kDor, 2.0f, 1);
        OvertravelAction a = OT_ACT_RECENTER;
        for (int i = 0; i < 1000 && a == OT_ACT_RECENTER; ++i)
            a = overtravel_update(&s, &c, fora + i * 0.005f, kDor, 2.0f, 1);   // longe, mas devagar
        ok("afastar devagar nao e fuga", a == OT_ACT_RECENTER);
    }

    // ---- FUGA: o unico caminho que ainda desarma -------------------------------------------
    //
    // Motor girando sozinho: rapido E ganhando distancia apesar da mola. Nenhum braco faz as duas
    // coisas ao mesmo tempo contra o motor -- por isso e este o criterio, e nao "nao parou a tempo".
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        c.mode = (uint8_t)OT_MODE_REARM;              // usuario pediu re-arme...
        const float fora = kDor + 0.85f;

        overtravel_update(&s, &c, fora, kDor, 40.0f, 1);          // dispara
        OvertravelAction a = OT_ACT_RECENTER;
        for (int i = 0; i < 2000 && a == OT_ACT_RECENTER; ++i)
            a = overtravel_update(&s, &c, fora + i * 0.04f, kDor, 40.0f, 1);   // acelerado, fugindo

        ok("fuga => desarma", a == OT_ACT_DISARM);
        ok("...e TRAVA, ignorando o modo re-armar", s.state == OT_ST_LOCKED);
        ok("registra que fomos nos", s.disarmed_by_us == 1);
    }

    // ---- fuga INTERROMPIDA nao soma: o relogio zera ----------------------------------------
    //
    // Uma sequencia de sustos nao e uma fuga. Se o relogio acumulasse entre eles, a base desarmaria
    // por soma de eventos inocentes espalhados no tempo.
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        const float fora = kDor + 0.85f;

        overtravel_update(&s, &c, fora, kDor, 40.0f, 1);
        OvertravelAction a = OT_ACT_RECENTER;
        for (int r = 0; r < 10 && a == OT_ACT_RECENTER; ++r) {
            for (int i = 0; i < 300 && a == OT_ACT_RECENTER; ++i)      // 300 ms de fuga (< 400)
                a = overtravel_update(&s, &c, fora + 1.5f + i * 0.04f, kDor, 40.0f, 1);
            a = overtravel_update(&s, &c, fora, kDor, 0.1f, 1);        // ...e a fuga cessa
        }
        ok("fuga interrompida nao acumula", a == OT_ACT_RECENTER);
        ok("relogio zerado", s.fuga_ticks == 0);
    }

    // ---- modo re-armar: volta so DENTRO do curso e PARADO ----------------------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        c.mode = (uint8_t)OT_MODE_REARM;
        const float fora = kDor + 0.85f;

        por_em_out(&s);

        ok("ainda fora do curso: nao re-arma",
           overtravel_update(&s, &c, fora, kDor, 0.0f, 1) == OT_ACT_HOLD);
        ok("dentro do curso mas GIRANDO: nao re-arma",
           overtravel_update(&s, &c, 0.0f, kDor, 20.0f, 1) == OT_ACT_HOLD);
        ok("dentro e parado: re-arma",
           overtravel_update(&s, &c, 0.0f, kDor, 0.0f, 1) == OT_ACT_NORMAL);
        ok("volta ao normal", s.state == OT_ST_NORMAL);
        ok("contou o re-arme", s.trips == 1);
    }

    // ---- O TESTE QUE FALTAVA: no modo TRAVAR, o disparo tem de ser contado -----------------
    //
    // `trips` so incrementa na RECUPERACAO (estado OUT -> NORMAL), e no modo travar essa transicao
    // nunca acontece — o estado vai direto para LOCKED. O relatorio de bancada usava `trips` como
    // "disparos" e imprimiu "disparou 0x neste boot" logo abaixo de mostrar um disparo real, com o
    // volante a -496,5 graus e o motor desarmado (15/08/2026). Um contador que nao conta estraga o
    // diagnostico seguinte, que e exatamente quando ele seria util.
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        c.mode = (uint8_t)OT_MODE_LOCK;
        const float fora = kDor + 0.85f;

        fugir_ate_desarmar(&s, &c, fora, kDor);

        ok("modo travar vai direto para LOCKED", s.state == OT_ST_LOCKED);
        ok("no modo travar NAO ha re-arme para contar", s.trips == 0);
        ok("mas o DISPARO foi contado", s.disparos == 1);
    }

    // ---- histerese: recuperar exige voltar ao curso, nao so sair da margem -----------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        c.mode = (uint8_t)OT_MODE_REARM;

        por_em_out(&s);                                          // desarmado, esperando voltar

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

        // Cada volta: desarmado esperando -> volta ao curso, parado -> re-arma. Na terceira, o teto.
        // (Ate 21/08/2026 este laco chamava fugir_ate_desarmar, que TRAVA de primeira desde que a
        // fuga passou a ignorar o modo — o teste passava sem nunca contar um re-arme sequer.)
        for (int i = 0; i < 3; ++i) {
            por_em_out(&s);
            overtravel_update(&s, &c, 0.0f, kDor, 0.0f, 1);    // volta ao curso, parado
        }
        ok("os dois primeiros re-armaram", s.trips == 3);
        ok("no terceiro disparo trava", s.state == OT_ST_LOCKED);
        ok("e nao volta mais",
           overtravel_update(&s, &c, 0.0f, kDor, 0.0f, 1) == OT_ACT_HOLD);
    }

    // ---- simetria: o lado negativo vale igual ---------------------------------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        ok("dispara tambem girando para o outro lado",
           overtravel_update(&s, &c, -(kDor + 0.85f), kDor, -10.0f, 1) == OT_ACT_RECENTER);
    }

    // ---- curso pequeno (formula, 360°): a margem nao pode engolir o curso ------------------
    {
        OvertravelState s; overtravel_init(&s);
        OvertravelCfg c = overtravel_default_cfg();
        const float dorF1 = 3.14159f;   // 360° totais => 180° de cada lado

        ok("no batente do formula nao dispara",
           overtravel_update(&s, &c, dorF1, dorF1, 0.0f, 1) == OT_ACT_NORMAL);
        ok("46 graus alem do batente do formula dispara",
           overtravel_update(&s, &c, dorF1 + 0.81f, dorF1, 5.0f, 1) == OT_ACT_RECENTER);
    }

    printf(falhas ? "\nFALHAS: %d\n" : "\nTUDO OK\n", falhas);
    return falhas ? 1 : 0;
}
