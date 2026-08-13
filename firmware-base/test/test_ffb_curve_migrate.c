// ============================================================================
//  DriveLab
//  test_ffb_curve_migrate.c — Testes de host da migracao da curva 5 -> 11 pontos.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "../inc/ffb_curve_migrate.h"
#include <stdio.h>

static int falhas = 0;
static void check(int c, const char* n) {
    if (c) printf("  ok     %s\n", n); else { printf("  FALHOU %s\n", n); falhas++; }
}

int main(void) {
    // 1) A GARANTIA: reta continua reta. Uma curva identidade de 5 pontos (0/25/50/75/100) tem de
    //    virar a identidade de 11 pontos — senao a migracao mudaria o feel de quem nunca mexeu.
    {
        int32_t antiga[5] = {0, 25, 50, 75, 100}, nova[11];
        ffb_curve_migrar_5_para_11(antiga, nova);
        int ok = 1;
        for (int i = 0; i < 11; ++i) if (nova[i] != i * 10) ok = 0;
        check(ok, "identidade de 5 pontos vira identidade de 11");
    }

    // 2) O CASO REAL da bancada (2026-08-12): 0/22/36/47/52 gravados quando os pontos eram
    //    0/25/50/75/100. Lidos SEM migrar, viravam a curva de 0 a 40% e amplificavam o pequeno.
    //    Migrados, os 11 pontos descrevem a MESMA funcao de antes.
    {
        int32_t antiga[5] = {0, 22, 36, 47, 52}, nova[11];
        ffb_curve_migrar_5_para_11(antiga, nova);
        // x=25 e x=50 caem exatamente em pontos antigos: tem de sair identicos.
        check(nova[0]  == 0,  "x=0 preservado");
        check(nova[5]  == 36, "x=50 cai no ponto antigo e nao muda");
        check(nova[10] == 52, "x=100 preservado");
        // x=10 fica a 40% do caminho entre 0 e 22 -> ~9. Era isto que virava 22 sem a migracao.
        check(nova[1] == 9,   "x=10 interpola para 9 (sem migrar sairia 22)");
        check(nova[2] == 18,  "x=20 interpola para 18 (sem migrar sairia 36)");
        // Monotonica: curva de forca que desce em algum trecho inverte a sensacao no volante.
        int cresce = 1;
        for (int i = 1; i < 11; ++i) if (nova[i] < nova[i-1]) cresce = 0;
        check(cresce, "resultado e monotonico");
    }

    // 3) Arredondamento: truncar puxaria a curva inteira para baixo, um pouco em cada ponto.
    {
        int32_t antiga[5] = {0, 10, 20, 30, 40}, nova[11];
        ffb_curve_migrar_5_para_11(antiga, nova);
        check(nova[1] == 4, "x=10 de 0->10 em 25% = 4,0 (arredonda, nao trunca)");
        check(nova[3] == 12, "x=30 = 12");
    }

    // 4) QUANDO migrar. So blob anterior aos pontos novos (ids 49-54). Blob que ja os tem foi
    //    salvo por firmware que conhece a grade de 11 — migrar de novo estragaria a curva.
    {
        check(ffb_curve_precisa_migrar(49, 49) == 1, "blob sem o ponto 49 -> migra");
        check(ffb_curve_precisa_migrar(48, 49) == 1, "blob mais antigo ainda -> migra");
        check(ffb_curve_precisa_migrar(55, 49) == 0, "blob que ja tem os 11 pontos -> NAO migra");
        check(ffb_curve_precisa_migrar(0, 49)  == 1, "sem blob valido -> chamador usa defaults");
    }

    // 5) IDEMPOTENCIA pelo blob: a migracao parte sempre do que esta GRAVADO, que nao muda. Rodar
    //    duas vezes sobre a mesma origem da o mesmo resultado — e o que permite nao gravar nada.
    {
        int32_t antiga[5] = {0, 22, 36, 47, 52}, a[11], b[11];
        ffb_curve_migrar_5_para_11(antiga, a);
        ffb_curve_migrar_5_para_11(antiga, b);
        int igual = 1;
        for (int i = 0; i < 11; ++i) if (a[i] != b[i]) igual = 0;
        check(igual, "migrar duas vezes a mesma origem da o mesmo resultado");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas); else printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
