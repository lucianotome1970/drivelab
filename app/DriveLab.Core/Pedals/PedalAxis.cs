// ============================================================================
//  DriveLab
//  PedalAxis.cs — Constante de resolução do eixo de pedal (12-bit) do contrato de protocolo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Pedals;

/// <summary>Formato do eixo de pedal do contrato: 12-bit (0..4095), como Simagic e o ADC do RP2040.</summary>
public static class PedalAxis
{
    public const int Resolution12Bit = 4095;
}
