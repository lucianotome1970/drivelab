// ============================================================================
//  DriveLab Firmware
//  dbg_ring.h — Buffer de debug em RAM, lido por SWD (substitui o CDC removido).
//  Com o redesign de compat com jogos (A0 em interface HID separada, sem CDC),
//  não há mais porta serial pra debug. Este ring buffer guarda texto em RAM;
//  lê-se por openocd:  dump_image / mdw <&g_dbgRing> ; o índice g_dbgHead diz
//  quantos bytes já foram escritos (módulo o tamanho do buffer).
//  NÃO-bloqueante e sem endpoint USB — seguro no caminho quente / callbacks.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#pragma once

#include <cstdint>

namespace drivelab
{
constexpr uint32_t kDbgRingSize = 2048;
extern char g_dbgRing[kDbgRingSize];   // openocd: dump destes bytes
extern volatile uint32_t g_dbgHead;    // total de bytes escritos (mod kDbgRingSize = posição)

// printf de debug NÃO-bloqueante: formata e grava no ring. Descartado se estourar
// o buffer temporário. Substitui o antigo dbgPrintf (que escrevia no CDC).
void dbgRingPrintf(const char *fmt, ...);
} // namespace drivelab
