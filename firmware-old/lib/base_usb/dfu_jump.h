// ============================================================================
//  DriveLab Firmware
//  dfu_jump.h — Salto para o bootloader de sistema da ST (EnterDfu),
//  extraído do monolito src/m05/main.cpp (M5 Stage 0, Task 3): magic em RAM
//  ".noinit" + reset de sistema + checagem no início do boot. Ver comentário
//  completo (histórico da tentativa 1 que falhou, motivo do .noinit vs
//  RTC->BKP0R, etc.) em dfu_jump.cpp junto de g_dfuMagic — mantido lá, não
//  duplicado aqui.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

// Checagem EnterDfu — TEM que ser a PRIMEIRÍSSIMA coisa de setup(), antes de
// QUALQUER init de USB (TinyUSBDevice.begin()/UsbBase::begin()) ou até de
// clock. Se g_dfuMagic (RAM ".noinit") tiver o magic, consome-o e salta pro
// bootloader de sistema (jumpToBootloaderEarly(), interno a dfu_jump.cpp) —
// não retorna nesse caso. Caso contrário, retorna normalmente (boot comum).
void dfuCheckAtBootOrJump();

// EnterDfu, passo 1 de 2 (A0 cmd=4) — chamar do loop() (nunca do callback
// SET_REPORT — nada de trabalho pesado/irreversível dentro do contexto do
// TinyUSB). Grava o magic em RAM ".noinit" e pede um reset de sistema de
// verdade (NVIC_SystemReset()); o salto de fato pro bootloader só acontece
// no PRÓXIMO boot, em dfuCheckAtBootOrJump() (chamada do início de setup()).
// Não retorna (NVIC_SystemReset() não retorna).
void dfuRequestJump();
