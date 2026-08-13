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
        check(c.drivable == 1,                "padrao e acionavel (A/B/Z)");
    }

    // 2) ABZ com resolucao informada aplica o CPR — vale para QUALQUER modelo,
    //    porque quadratura e quadratura e os quatro sensores tem saida A/B/Z.
    {
        EncoderConfig c = encoder_config_from_settings(ENC_MODEL_E6B2, ENC_IFACE_ABZ, 10000);
        check(c.mode == ENC_MODE_INCREMENTAL, "ABZ = incremental");
        check(c.cpr  == 10000,                "aplica a resolucao informada");
        check(c.drivable == 1,                "ABZ e acionavel");

        EncoderConfig m = encoder_config_from_settings(ENC_MODEL_MT6835, ENC_IFACE_ABZ, 4096);
        check(m.drivable == 1,                "magnetico em ABZ tambem e acionavel");
        check(m.mode == ENC_MODE_INCREMENTAL, "magnetico em ABZ le como incremental");
    }

    // 3) AS5047P em SPI e o unico absoluto que a base sabe ler (familia AMS).
    {
        EncoderConfig c = encoder_config_from_settings(ENC_MODEL_AS5047P, ENC_IFACE_SPI, 16384);
        check(c.mode == ENC_MODE_SPI_ABS_AMS, "AS5047P em SPI = absoluto AMS");
        check(c.cpr  == 16384,                "resolucao do absoluto");
        check(c.drivable == 1,                "AS5047P em SPI e acionavel");
    }

    // 4) O BUG QUE ISTO CONSERTA. MT6701 em SSI e MT6835 em SPI nao tem driver —
    //    nem nosso, nem do ODrive. Antes, a escolha gravava a resolucao do
    //    magnetico (16384) por cima de uma leitura A/B/Z: a base seguia girando e
    //    reportando angulo errado por um fator de quatro. Agora nao aplica NADA.
    {
        EncoderConfig a = encoder_config_from_settings(ENC_MODEL_MT6701, ENC_IFACE_SSI, 16384);
        check(a.drivable == 0, "MT6701 em SSI: sem driver, nao aciona");

        EncoderConfig b = encoder_config_from_settings(ENC_MODEL_MT6835, ENC_IFACE_SPI, 2097152u);
        check(b.drivable == 0, "MT6835 em SPI: sem driver, nao aciona");

        // Um sensor sem modelo declarado, ligado por SPI, tambem nao passa:
        // "absoluto por SPI" nao e um protocolo unico.
        EncoderConfig g = encoder_config_from_settings(ENC_MODEL_GENERICO, ENC_IFACE_SPI, 16384);
        check(g.drivable == 0, "generico em SPI: protocolo desconhecido, nao aciona");
    }

    // 5) Resolucao de 21 bits cabe no campo (MT6835: 2.097.152). O campo aguenta
    //    mesmo que o driver ainda nao exista — quando existir, nao precisa mudar.
    {
        EncoderConfig c = encoder_config_from_settings(ENC_MODEL_MT6835, ENC_IFACE_ABZ, 2097152u);
        check(c.cpr == 2097152u, "21 bits cabem no campo");
    }

    // 6) Resolucao zero NUNCA vira CPR zero. Zero significa "nao informado"; um CPR
    //    zerado quebraria a conversao de posicao (divisao por zero).
    {
        EncoderConfig c = encoder_config_from_settings(ENC_MODEL_E6B2, ENC_IFACE_ABZ, 0);
        check(c.cpr == 0, "resolucao zero = nao aplicar, nao gravar zero");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas); else printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
