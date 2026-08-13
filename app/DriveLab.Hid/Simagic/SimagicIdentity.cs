// ============================================================================
//  DriveLab
//  SimagicIdentity.cs — VID/PID USB do Simagic P2000 (e família).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Hid.Simagic;

/// <summary>USB do Simagic P2000 (e família). Adicionar mais PIDs conforme conhecermos (P1000 etc.).</summary>
public static class SimagicIdentity
{
    public const int VendorId = 0x0483;
    public const int ProductId = 0x0524;
}
