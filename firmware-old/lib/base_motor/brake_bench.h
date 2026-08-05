/*
 * brake_bench.h — mapa puro do comando de bancada do brake chopper (cmd=8, BrakeBench).
 *
 * Traduz o arg do A0 em {armar, duty%, sair do modo bancada}, com teto de 20% (proteção contra engano).
 * Puro e host-testável (sem Arduino). A ação real no hardware (arm/disarm/setDuty do FocBrake) fica no m5.
 *
 * arg: 0 = desarma / sai · 1 = arma em 0% · 2..20 = arma em arg% · >20 = clampa em 20%.
 *
 * Autor: Luciano Tomé
 * Licença: MIT
 */
#pragma once
#include <cstdint>

struct BrakeBenchState {
    bool    armed;      ///< true = chopper armado
    uint8_t dutyPct;    ///< duty manual em % (0..20)
    bool    exitBench;  ///< true = sair do modo bancada (desarmar)
};

inline BrakeBenchState brakeBenchCommand(uint8_t arg) {
    if (arg == 0) return BrakeBenchState{ false, 0, true };
    if (arg == 1) return BrakeBenchState{ true, 0, false };
    uint8_t d = (arg > 20) ? 20 : arg;
    return BrakeBenchState{ true, d, false };
}
