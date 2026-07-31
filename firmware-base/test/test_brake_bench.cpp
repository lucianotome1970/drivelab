/*
 * test_brake_bench.cpp — testes host do mapa do comando de bancada do brake chopper.
 * Autor: Luciano Tomé
 * Licença: MIT
 */
#include "brake_bench.h"
#include <cassert>
#include <cstdio>

int main() {
    // arg=0 → desarma / sai do modo bancada
    {
        BrakeBenchState s = brakeBenchCommand(0);
        assert(!s.armed && s.dutyPct == 0 && s.exitBench);
    }
    // arg=1 → arma em 0%
    {
        BrakeBenchState s = brakeBenchCommand(1);
        assert(s.armed && s.dutyPct == 0 && !s.exitBench);
    }
    // arg=2 → arma em 2%
    {
        BrakeBenchState s = brakeBenchCommand(2);
        assert(s.armed && s.dutyPct == 2 && !s.exitBench);
    }
    // arg=5 → 5%
    { assert(brakeBenchCommand(5).dutyPct == 5); }
    // arg=20 → 20% (limite exato)
    { BrakeBenchState s = brakeBenchCommand(20); assert(s.armed && s.dutyPct == 20 && !s.exitBench); }
    // arg=21 → clampa em 20%
    { assert(brakeBenchCommand(21).dutyPct == 20); }
    // arg=50 → clampa em 20%
    { assert(brakeBenchCommand(50).dutyPct == 20); }
    // arg=255 → clampa em 20% e continua armado
    { BrakeBenchState s = brakeBenchCommand(255); assert(s.armed && s.dutyPct == 20 && !s.exitBench); }

    printf("test_brake_bench OK\n");
    return 0;
}
