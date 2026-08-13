// ============================================================================
//  DriveLab
//  test_osc_guard.c — Testes de host da guarda de oscilacao.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "../inc/osc_guard.h"
#include <stdio.h>
#include <math.h>

static int falhas = 0;
static void check(int c, const char* n) {
    if (c) printf("  ok     %s\n", n); else { printf("  FALHOU %s\n", n); falhas++; }
}

// Roda `ms` de uma senoide de velocidade a `hz`, devolvendo o MAIOR nivel de guarda alcancado.
static float simular(OscGuard* g, float hz, float amplitude, int ms, int habilitada) {
    float maior = 0.0f;
    for (int t = 0; t < ms; ++t) {
        const float vel = amplitude * sinf(2.0f * 3.14159265f * hz * (float)t / 1000.0f);
        osc_guard_update(g, vel, 1, habilitada);
        if (g->nivel > maior) maior = g->nivel;
    }
    return maior;
}

int main(void) {
    // 1) A GARANTIA: desligada, nao existe. Zero torque e estado limpo, sempre — e o default do
    //    setting e 0, entao e este o caminho de quem nunca ligar.
    {
        OscGuard g; osc_guard_reset(&g);
        float soma = 0.0f;
        for (int t = 0; t < 2000; ++t) soma += fabsf(osc_guard_update(&g, sinf((float)t) * 5.0f, 1, 0));
        check(soma == 0.0f, "desligada nao aplica torque nenhum");
        check(g.nivel == 0.0f, "desligada nao acumula estado");
    }

    // 2) OSCILACAO de 15 Hz — a faixa em que um volante DD se auto-alimenta. Tem de agir.
    {
        OscGuard g; osc_guard_reset(&g);
        const float nivel = simular(&g, 15.0f, 3.0f, 600, 1);
        check(nivel > 0.9f, "oscilacao de 15 Hz aciona a guarda");
    }

    // 3) VOLANTE PARADO: o sinal da velocidade troca por ruido de quantizacao do encoder, e sem o
    //    piso de velocidade a guarda dispararia com o volante em repouso — amortecendo o nada.
    {
        OscGuard g; osc_guard_reset(&g);
        float maior = 0.0f;
        for (int t = 0; t < 2000; ++t) {
            const float ruido = ((t % 2) ? 0.1f : -0.1f);   // troca de sinal todo tick, mas minusculo
            osc_guard_update(&g, ruido, 1, 1);
            if (g.nivel > maior) maior = g.nivel;
        }
        check(maior == 0.0f, "ruido de encoder com volante parado NAO aciona");
    }

    // 4) PILOTO DIRIGINDO: esterco de um lado para o outro, mas devagar (0,5 Hz). Se a guarda
    //    entrasse aqui, ela roubaria forca no meio de uma curva — o oposto do que se quer.
    {
        OscGuard g; osc_guard_reset(&g);
        const float nivel = simular(&g, 0.5f, 4.0f, 6000, 1);
        check(nivel == 0.0f, "esterco normal do piloto NAO aciona");
    }

    // 5) ZEBRA / TEXTURA: vibracao rapida de PEQUENA amplitude, que nao chega a inverter o sentido
    //    do volante. E o detalhe que o piloto quer sentir, e a guarda tem de ficar fora.
    {
        OscGuard g; osc_guard_reset(&g);
        const float nivel = simular(&g, 40.0f, 0.2f, 1000, 1);   // abaixo do piso de velocidade
        check(nivel == 0.0f, "textura de baixa amplitude NAO aciona");
    }

    // 6) O torque SEMPRE se opoe ao movimento — uma guarda que empurrasse a favor realimentaria
    //    exatamente a oscilacao que deveria matar.
    {
        OscGuard g; osc_guard_reset(&g);
        simular(&g, 15.0f, 3.0f, 600, 1);
        const float t_pos = osc_guard_update(&g, +2.0f, 1, 1);
        const float t_neg = osc_guard_update(&g, -2.0f, 1, 1);
        check(t_pos < 0.0f, "velocidade positiva -> torque negativo");
        check(t_neg > 0.0f, "velocidade negativa -> torque positivo");
    }

    // 7) SAIDA SUAVE, com HISTERESE deliberada. Cessada a oscilacao, a guarda ainda espera o
    //    episodio "esfriar" (2x o intervalo, ~240 ms) antes de comecar a soltar, e so entao decai
    //    devagar. Os dois atrasos sao de proposito: soltar no primeiro tick sem reversao faria a
    //    guarda piscar entre cada meio-ciclo da propria oscilacao que esta cortando, e cair de uma
    //    vez devolveria o solavanco que ela acabou de evitar.
    {
        OscGuard g; osc_guard_reset(&g);
        simular(&g, 15.0f, 3.0f, 600, 1);
        const float antes = g.nivel;

        for (int t = 0; t < 50; ++t) osc_guard_update(&g, 0.0f, 1, 1);   // volante parou agora
        check(g.nivel == antes, "logo apos parar ainda segura (histerese, nao solta no susto)");

        for (int t = 0; t < 300; ++t) osc_guard_update(&g, 0.0f, 1, 1);  // passou o esfriamento
        check(g.nivel < antes, "passado o esfriamento, o nivel decai");
        check(g.nivel > 0.0f,  "e decai SUAVE, nao de uma vez");

        for (int t = 0; t < 1000; ++t) osc_guard_update(&g, 0.0f, 1, 1);
        check(g.nivel == 0.0f, "chega a zero se ficar parado");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas); else printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
