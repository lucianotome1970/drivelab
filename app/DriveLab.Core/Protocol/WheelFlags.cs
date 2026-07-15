// ============================================================================
//  DriveLab
//  WheelFlags.cs — Bits de estado do rim (calibrado, erro, simulador).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

[Flags]
public enum WheelFlags : byte
{
    None = 0,
    Calibrated = 1,
    Error = 2,
    UsingSimulator = 4,
}
