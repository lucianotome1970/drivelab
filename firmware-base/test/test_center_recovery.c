// ============================================================================
//  DriveLab
//  test_center_recovery.c — Testes de host da recuperacao de centro no boot.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "../inc/center_recovery.h"
#include <stdio.h>

static int falhas = 0;
static void check(int c, const char* n) {
    if (c) printf("  ok     %s\n", n); else { printf("  FALHOU %s\n", n); falhas++; }
}
static int perto(float a, float b) {
    const float d = a - b;
    return (d < 0.001f) && (d > -0.001f);
}

// MT6701 / AS5047P: 14 bits
#define CPR 16384u

int main(void) {
    // 1) O CASO REAL. O volante ficou parado no rig entre uma sessao e outra: a
    //    contagem lida no boot e a mesma de quando desligamos. Recupera exato.
    {
        CenterRecovery r = center_recover(CPR, 4000, 1.25f, 4000 + (uint32_t)(0.25f * CPR));
        check(perto(r.pos_turns, 1.25f), "volante parado: recupera a posicao exata");
        check(r.trusted == 1,            "volante parado: confiavel");
    }

    // 2) Centrado e parado: volta zero.
    {
        CenterRecovery r = center_recover(CPR, 4000, 0.0f, 4000);
        check(perto(r.pos_turns, 0.0f), "no centro: zero");
        check(r.trusted == 1,           "no centro: confiavel");
    }

    // 3) Alguem encostou no aro: um oitavo de volta. Reconstroi o movimento real
    //    e segue confiavel — e o caso de esbarrar no volante, nao de girar.
    {
        const float deixado = 0.75f;
        CenterRecovery r = center_recover(CPR, 4000, deixado,
                                          4000 + (uint32_t)((0.75f + 0.125f) * CPR));
        check(perto(r.pos_turns, 0.875f), "encostaram no aro: acompanha o movimento");
        check(r.trusted == 1,             "movimento pequeno: ainda confiavel");
    }

    // 4) O CASO QUE PROTEGE O PILOTO. Giraram quase meia volta com a base
    //    desligada. "Andou +0,45" e "andou -0,55" produzem a MESMA leitura, e nao
    //    ha como desempatar. Errar aqui joga o batente uma volta inteira para o
    //    lado errado — entao a resposta e "nao sei".
    {
        CenterRecovery r = center_recover(CPR, 4000, 0.0f, 4000 + (uint32_t)(0.45f * CPR));
        check(r.trusted == 0, "quase meia volta: ambiguo, pede recentralizacao");
    }

    // 5) A posicao reconstruida NUNCA fica a mais de meia volta de onde deixamos —
    //    e a propriedade que define o metodo. Aqui o volante passou do zero para
    //    o outro lado do circulo: a resposta certa e -0,05 volta, nao +0,95.
    {
        CenterRecovery r = center_recover(CPR, 4000, 0.0f,
                                          4000 + (uint32_t)(0.95f * CPR));
        check(perto(r.pos_turns, -0.05f), "cruzar o zero: escolhe a volta vizinha, nao a distante");
        check(r.trusted == 1,             "cruzar o zero por pouco: confiavel");
    }

    // 6) Volta negativa: o volante foi deixado virado para o outro lado, alem de
    //    uma volta inteira. O curso do volante e +-1,25 volta, entao isto e comum.
    {
        CenterRecovery r = center_recover(CPR, 4000, -1.25f,
                                          4000 + (uint32_t)(0.75f * CPR));
        check(perto(r.pos_turns, -1.25f), "posicao negativa alem de uma volta");
        check(r.trusted == 1,             "negativa e parada: confiavel");
    }

    // 7) O centro nao precisa cair em contagem redonda, e a diferenca pode dar a
    //    volta pelo zero do sensor (center perto do fim da escala).
    {
        CenterRecovery r = center_recover(CPR, 16000, 0.0f, 100);
        // 100 esta 500 contagens ADIANTE de 16000 (deu a volta): 500/16384 = 0,0305
        check(perto(r.pos_turns, 500.0f / (float)CPR), "centro perto do fim da escala: da a volta certo");
        check(r.trusted == 1,                          "e continua confiavel");
    }

    // 8) Sem sensor absoluto (cpr = 0) nao ha o que reconstruir. Nunca devolver
    //    "confiavel" por omissao: quem chamou tem de pedir recentralizacao.
    {
        CenterRecovery r = center_recover(0, 0, 1.0f, 0);
        check(r.trusted == 0,          "sem sensor absoluto: nao confiavel");
        check(perto(r.pos_turns, 0.0f), "sem sensor absoluto: nao inventa posicao");
    }

    // 9) MT6835 em 21 bits: a mesma conta tem de valer na resolucao alta, sem
    //    estourar o intermediario.
    {
        const uint32_t cpr21 = 2097152u;
        CenterRecovery r = center_recover(cpr21, 1000000u, 2.5f,
                                          1000000u + (uint32_t)(0.5f * cpr21));
        check(perto(r.pos_turns, 2.5f), "21 bits: recupera igual");
        check(r.trusted == 1,           "21 bits: confiavel");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas); else printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
