// ============================================================================
//  DriveLab
//  test_brake_meter.c — Testes de host do medidor do brake chopper.
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================
#include "../inc/brake_meter.h"
#include <stdio.h>

static int falhas = 0;

static void check(int cond, const char* nome) {
    if (cond) { printf("  ok     %s\n", nome); }
    else      { printf("  FALHOU %s\n", nome); falhas++; }
}

// 24 V sobre 2 ohm com duty 1.0 = 288 W
#define VBUS 24.0f
#define R     2.0f
#define DT   (1.0f / 8000.0f)

int main(void) {
    // 1) duty zero nao acumula nem conta
    {
        BrakeMeter m; brake_meter_init(&m);
        for (int i = 0; i < 8000; i++) brake_meter_update(&m, 0.0f, VBUS, R, DT);
        check(m.energy_mj == 0,   "duty zero nao acumula energia");
        check(m.activations == 0, "duty zero nao conta acionamento");
        check(m.peak_dw == 0,     "duty zero nao registra pico");
    }

    // 2) energia integra certo: 288 W por 1 s = 288 J = 288000 mJ
    {
        BrakeMeter m; brake_meter_init(&m);
        for (int i = 0; i < 8000; i++) brake_meter_update(&m, 1.0f, VBUS, R, DT);
        long erro = (long)m.energy_mj - 288000L;
        if (erro < 0) erro = -erro;
        check(erro < 300, "288 W por 1 s da ~288000 mJ");
    }

    // 3) um evento de frenagem conta UMA vez, nao uma por ciclo
    {
        BrakeMeter m; brake_meter_init(&m);
        for (int i = 0; i < 100; i++) brake_meter_update(&m, 0.5f, VBUS, R, DT);
        check(m.activations == 1, "conducao continua conta 1 acionamento");
        for (int i = 0; i < 100; i++) brake_meter_update(&m, 0.0f, VBUS, R, DT);
        for (int i = 0; i < 100; i++) brake_meter_update(&m, 0.5f, VBUS, R, DT);
        check(m.activations == 2, "segundo evento conta o segundo acionamento");
    }

    // 4) PRECISAO LONGA: depois de muitos joules, incremento pequeno ainda conta
    {
        BrakeMeter m; brake_meter_init(&m);
        // acumula ~2880 J (10 s a 288 W)
        for (int i = 0; i < 80000; i++) brake_meter_update(&m, 1.0f, VBUS, R, DT);
        uint32_t antes = m.energy_mj;
        check(antes > 2800000u, "acumulou os ~2880 J iniciais");
        // agora 1 s de potencia BAIXA (duty 0.01 = 2,88 W = 2880 mJ)
        for (int i = 0; i < 8000; i++) brake_meter_update(&m, 0.01f, VBUS, R, DT);
        uint32_t delta = m.energy_mj - antes;
        check(delta > 2500u && delta < 3300u,
              "incremento pequeno ainda conta depois de milhares de joules");
    }

    // 5) o pico guarda o maximo e nao e apagado depois
    {
        BrakeMeter m; brake_meter_init(&m);
        brake_meter_update(&m, 1.0f, VBUS, R, DT);     // 288 W -> 2880 decimos de watt
        uint16_t pico = m.peak_dw;
        check(pico > 2800 && pico < 2960, "pico registra ~288 W");
        for (int i = 0; i < 100; i++) brake_meter_update(&m, 0.1f, VBUS, R, DT);
        check(m.peak_dw == pico, "potencia menor depois nao apaga o pico");
    }

    if (falhas) printf("\nFALHOU: %d\n", falhas);
    else        printf("\nTUDO OK\n");
    return falhas ? 1 : 0;
}
