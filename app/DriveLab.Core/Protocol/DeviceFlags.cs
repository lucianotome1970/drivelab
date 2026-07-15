// ============================================================================
//  DriveLab
//  DeviceFlags.cs — Bits de flags de estado do dispositivo (força habilitada, calibrado, erro, simulador).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

[Flags]
public enum DeviceFlags : byte
{
    None = 0,
    ForceEnabled = 1,
    Calibrated = 2,
    Error = 4,
    UsingSimulator = 8,
}
