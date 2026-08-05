// ============================================================================
//  DriveLab Firmware
//  dbg_ring.cpp — Implementação do ring buffer de debug em RAM (ver dbg_ring.h).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================
#include "dbg_ring.h"

#include <cstdarg>
#include <cstdio>

namespace drivelab
{
char g_dbgRing[kDbgRingSize] = {0};
volatile uint32_t g_dbgHead = 0;

void dbgRingPrintf(const char *fmt, ...)
{
    char tmp[192];
    va_list ap;
    va_start(ap, fmt);
    int n = vsnprintf(tmp, sizeof(tmp), fmt, ap);
    va_end(ap);
    if (n <= 0) return;
    if (n > (int)sizeof(tmp)) n = (int)sizeof(tmp);
    for (int i = 0; i < n; ++i)
    {
        g_dbgRing[g_dbgHead % kDbgRingSize] = tmp[i];
        g_dbgHead++;
    }
}
} // namespace drivelab
