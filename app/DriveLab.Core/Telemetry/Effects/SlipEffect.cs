// ============================================================================
//  DriveLab
//  SlipEffect.cs — Vibração quando as rodas perdem aderência (amplitude escala com o escorregamento).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;

namespace DriveLab.Core.Telemetry.Effects;

/// <summary>
/// Escorregamento: quando <see cref="GameTelemetry.Slip"/> passa do limiar, emite uma senoide cuja amplitude
/// cresce com o slip (até <see cref="SlipMax"/>). Ajuda a sentir a perda de aderência (frente/traseira).
/// </summary>
public sealed class SlipEffect : ITelemetryEffect
{
    public string Name => "Slip";
    public bool Enabled { get; set; } = true;
    public float Gain { get; set; } = 0.4f;

    /// <summary>Slip mínimo para acionar.</summary>
    public float Threshold { get; set; } = 0.15f;

    /// <summary>Slip que corresponde à amplitude máxima.</summary>
    public float SlipMax { get; set; } = 1.0f;

    /// <summary>Frequência da vibração (Hz).</summary>
    public float VibHz { get; set; } = 40f;

    public float Compute(GameTelemetry t, double timeSeconds)
    {
        if (!t.HasData || t.Slip <= Threshold) return 0f;
        float amp = Math.Clamp((t.Slip - Threshold) / Math.Max(1e-4f, SlipMax - Threshold), 0f, 1f);
        return amp * (float)Math.Sin(2 * Math.PI * VibHz * timeSeconds);
    }
}
