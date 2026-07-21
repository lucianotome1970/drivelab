// ============================================================================
//  DriveLab
//  RpmRumbleEffect.cs — Vibração de motor que escala com o RPM (frequência e amplitude sobem com o RPM).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;

namespace DriveLab.Core.Telemetry.Effects;

/// <summary>
/// Rumble de motor: uma senoide cuja amplitude cresce da fração <see cref="StartFraction"/> do RPM até o
/// redline, e cuja frequência sobe com o RPM (sensação de motor "trabalhando"). Sutil por padrão.
/// </summary>
public sealed class RpmRumbleEffect : ITelemetryEffect
{
    public string Name => "RPM";
    public bool Enabled { get; set; } = true;
    public float Gain { get; set; } = 0.15f;

    /// <summary>Fração do RPM em que o rumble começa (abaixo disso, silêncio).</summary>
    public float StartFraction { get; set; } = 0.30f;

    /// <summary>Frequência (Hz) no início e no redline — interpolada pela fração do RPM.</summary>
    public float MinHz { get; set; } = 25f;
    public float MaxHz { get; set; } = 60f;

    public float Compute(GameTelemetry t, double timeSeconds)
    {
        if (!t.HasData || t.MaxRpm <= 0f) return 0f;
        float frac = t.Rpm / t.MaxRpm;
        if (frac <= StartFraction) return 0f;

        float amp = Math.Clamp((frac - StartFraction) / (1f - StartFraction), 0f, 1f);
        float hz = MinHz + (MaxHz - MinHz) * Math.Clamp(frac, 0f, 1f);
        return amp * (float)Math.Sin(2 * Math.PI * hz * timeSeconds);
    }
}
