// ============================================================================
//  DriveLab Firmware
//  ffb_arm_guard.h — Predicados PUROS de segurança do arme do modo "FFB do
//  jogo" (M6): quando pode entrar e quando tem que desarmar. Sem Arduino →
//  host-testável (ver test/test_ffb_arm_guard.cpp). A máquina de estados FOC
//  em si (src/m5/main.cpp) chama estes predicados.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

// Só pode ARMAR (entrar no modo FFB do jogo) se o motor já foi calibrado.
inline bool canEnterGameFfb(bool motorReady) { return motorReady; }

// Deve DESARMAR (sair do modo, motor.disable()) se qualquer condição de saída ocorrer.
inline bool shouldDisarmGameFfb(bool runaway, bool fault, bool usbMounted, bool forceEnabled)
{
    return runaway || fault || !usbMounted || !forceEnabled;
}
