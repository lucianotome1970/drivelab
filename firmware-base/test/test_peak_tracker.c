// ============================================================================
//  DriveLab
//  test_peak_tracker.c — Testes de host do rastreador de picos.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "../inc/peak_tracker.h"
#include <stdio.h>

static int falhas = 0;

static void check(int cond, const char* nome) {
    if (cond) { printf("  ok     %s\n", nome); }
    else      { printf("  FALHOU %s\n", nome); falhas++; }
}

int main(void) {
    // 1) nasce zerado
    {
        PeakTracker p; peak_tracker_init(&p);
        check(p.pos == 0 && p.neg == 0, "nasce com os dois picos em zero");
    }

    // 2) positivo e negativo sao INDEPENDENTES
    {
        PeakTracker p; peak_tracker_init(&p);
        peak_tracker_update(&p, 1500);
        peak_tracker_update(&p, -800);
        check(p.pos == 1500, "guarda o pico positivo");
        check(p.neg == -800, "guarda o pico negativo, sem virar modulo");
    }

    // 3) valor menor depois NAO apaga o pico
    {
        PeakTracker p; peak_tracker_init(&p);
        peak_tracker_update(&p, 2000);
        peak_tracker_update(&p, 10);
        peak_tracker_update(&p, 0);
        check(p.pos == 2000, "positivo menor depois nao apaga o pico");

        peak_tracker_update(&p, -2000);
        peak_tracker_update(&p, -10);
        check(p.neg == -2000, "negativo menor depois nao apaga o pico");
    }

    // 4) so um lado ativo deixa o outro em zero — e e isso que denuncia assimetria
    {
        PeakTracker p; peak_tracker_init(&p);
        for (int i = 0; i < 100; i++) peak_tracker_update(&p, 500 + i);
        check(p.pos == 599 && p.neg == 0, "forca so num sentido deixa o outro pico em zero");
    }

    // 5) satura em vez de estourar o int16
    {
        PeakTracker p; peak_tracker_init(&p);
        peak_tracker_update(&p,  40000);
        peak_tracker_update(&p, -40000);
        check(p.pos == 32767,  "satura no maximo do int16");
        check(p.neg == -32768, "satura no minimo do int16");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas);
    else        printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
