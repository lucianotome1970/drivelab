// ============================================================================
//  DriveLab Firmware
//  test_ffb_arm_guard.cpp — Teste host dos predicados de segurança do arme do
//  modo "FFB do jogo" (M6). Puros, sem placa.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include <cassert>
#include <cstdio>
#include "ffb_arm_guard.h"

int main()
{
    // Só entra no modo do jogo se o motor foi calibrado.
    assert(canEnterGameFfb(true)  == true);
    assert(canEnterGameFfb(false) == false);

    // Desarma se QUALQUER condição de saída: runaway, fault, USB caiu, ou app desabilitou.
    assert(shouldDisarmGameFfb(false, false, true,  true)  == false); // tudo ok → mantém
    assert(shouldDisarmGameFfb(true,  false, true,  true)  == true);  // runaway
    assert(shouldDisarmGameFfb(false, true,  true,  true)  == true);  // fault
    assert(shouldDisarmGameFfb(false, false, false, true)  == true);  // USB caiu
    assert(shouldDisarmGameFfb(false, false, true,  false) == true);  // app desabilitou
    std::printf("test_ffb_arm_guard: OK\n");
    return 0;
}
