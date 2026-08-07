// ============================================================================
//  DriveLab
//  test_encoder_config.c — Testes de host da traducao settings -> encoder.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "../inc/encoder_config.h"
#include <stdio.h>

static int falhas = 0;
static void check(int c, const char* n) {
    if (c) printf("  ok     %s\n", n); else { printf("  FALHOU %s\n", n); falhas++; }
}

int main(void) {
    // 1) A GARANTIA. Com os padroes, sai a configuracao de HOJE — a unica baseline
    //    validada (4 voltas em 2026-08-06). Se este teste falhar, PARE: a base mudou
    //    de comportamento sem ninguem pedir.
    {
        EncoderConfig c = encoder_config_from_settings(0, 0, 0);
        check(c.mode == ENC_MODE_INCREMENTAL, "padrao = incremental (o de hoje)");
        check(c.cpr  == 0,                    "padrao nao mexe no CPR (mantem o do ODrive)");
        check(c.use_index == 0,               "padrao nao liga o index");
    }

    // 2) ABZ com resolucao informada aplica o CPR
    {
        EncoderConfig c = encoder_config_from_settings(1 /*E6B2*/, ENC_IFACE_ABZ, 10000);
        check(c.mode == ENC_MODE_INCREMENTAL, "ABZ = incremental");
        check(c.cpr  == 10000,                "aplica a resolucao informada");
    }

    // 3) SPI e SSI viram modo absoluto
    {
        EncoderConfig a = encoder_config_from_settings(4 /*AS5047P*/, ENC_IFACE_SPI, 16384);
        check(a.mode == ENC_MODE_SPI_ABS, "SPI = absoluto");
        check(a.cpr  == 16384,            "resolucao do absoluto");

        EncoderConfig b = encoder_config_from_settings(2 /*MT6701*/, ENC_IFACE_SSI, 16384);
        check(b.mode == ENC_MODE_SPI_ABS, "SSI tambem e absoluto");
    }

    // 4) Resolucao de 21 bits cabe (MT6835 em SPI: 2.097.152)
    {
        EncoderConfig c = encoder_config_from_settings(3 /*MT6835*/, ENC_IFACE_SPI, 2097152u);
        check(c.cpr == 2097152u, "21 bits cabem no campo");
    }

    // 5) Resolucao zero NUNCA vira CPR zero. Zero significa "nao informado"; um CPR
    //    zerado quebraria a conversao de posicao (divisao por zero).
    {
        EncoderConfig c = encoder_config_from_settings(1, ENC_IFACE_ABZ, 0);
        check(c.cpr == 0, "resolucao zero = nao aplicar, nao gravar zero");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas); else printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
