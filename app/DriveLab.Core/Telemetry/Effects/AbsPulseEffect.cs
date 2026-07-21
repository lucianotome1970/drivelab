// ============================================================================
//  DriveLab
//  AbsPulseEffect.cs — Pulso no volante quando o ABS atua (onda quadrada escalada pela intensidade do ABS).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;

namespace DriveLab.Core.Telemetry.Effects;

/// <summary>
/// ABS pulsante: enquanto o ABS está atuando (<see cref="GameTelemetry.Abs"/> acima do limiar), emite uma onda
/// quadrada (pulso seco) cuja amplitude escala com a intensidade do ABS. Frequência fixa (sensação de "tec-tec").
/// </summary>
public sealed class AbsPulseEffect : ITelemetryEffect
{
    public string Name => "ABS";
    public bool Enabled { get; set; } = true;
    public float Gain { get; set; } = 0.6f;

    /// <summary>Intensidade mínima de ABS para acionar.</summary>
    public float Threshold { get; set; } = 0.02f;

    /// <summary>Frequência do pulso (Hz).</summary>
    public float PulseHz { get; set; } = 18f;

    public float Compute(GameTelemetry t, double timeSeconds)
    {
        if (!t.HasData || t.Abs <= Threshold) return 0f;
        float amp = Math.Clamp(t.Abs, 0f, 1f);
        double phase = (timeSeconds * PulseHz) % 1.0;   // 0..1
        float square = phase < 0.5 ? 1f : -1f;
        return amp * square;
    }
}
