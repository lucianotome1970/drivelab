// ============================================================================
//  DriveLab
//  test_thermal_config.c — Testes de host da traducao do limite termico dos FETs.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "../inc/thermal_config.h"
#include <stdio.h>

static int falhas = 0;
static void check(int c, const char* n) {
    if (c) printf("  ok     %s\n", n); else { printf("  FALHOU %s\n", n); falhas++; }
}

int main(void) {
    // 1) O PADRAO DO APP (85 °C). Reduz a partir de 85, zera em 105, e o eixo
    //    desarma em 110 (o +5 e do proprio ODrive).
    {
        ThermalConfig t = thermal_config_from_settings(85);
        check(t.lower == 85.0f,  "85 = onde a reducao COMECA");
        check(t.upper == 105.0f, "corte total 20 °C acima");
        check(t.apply == 1,      "valor informado -> aplica");
    }

    // 2) A GARANTIA. Zero e "nao informado": mantem o que o ODrive tem de fabrica
    //    (100/120). Nunca gravar zero, que desligaria a protecao inteira.
    {
        ThermalConfig t = thermal_config_from_settings(0);
        check(t.apply == 0, "zero = nao informado, nao mexe na protecao");
    }

    // 3) A reducao SEMPRE comeca antes do corte — a faixa nunca pode inverter nem
    //    zerar. Faixa zero faria a corrente cair de cheia para nada de uma vez,
    //    e o proprio ODrive dividiria por zero no calculo do derating.
    {
        for (int c = 50; c <= 110; c += 10) {
            ThermalConfig t = thermal_config_from_settings((uint8_t)c);
            if (t.upper <= t.lower) { check(0, "faixa invertida ou nula"); break; }
        }
        check(1, "em toda a faixa do app (50-110), corte sempre acima da reducao");
    }

    // 4) Os extremos do que o app deixa escolher passam intactos.
    {
        ThermalConfig frio = thermal_config_from_settings(50);
        check(frio.lower == 50.0f && frio.upper == 70.0f, "minimo do app (50)");

        ThermalConfig quente = thermal_config_from_settings(110);
        check(quente.lower == 110.0f && quente.upper == 130.0f, "maximo do app (110)");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas); else printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
