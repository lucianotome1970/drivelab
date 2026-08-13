// ============================================================================
//  DriveLab
//  HandbrakeFlags.cs — Bits do campo Flags do PedalState quando usado pelo freio de mão.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

/// <summary>Bits do campo Flags do PedalState quando usado pelo freio de mão.</summary>
[Flags]
public enum HandbrakeFlags : byte
{
    None = 0,
    ButtonPressed = 1,
}
