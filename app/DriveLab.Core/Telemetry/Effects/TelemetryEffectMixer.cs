// ============================================================================
//  DriveLab
//  TelemetryEffectMixer.cs — Soma os efeitos de telemetria ligados (× ganho) numa força aditiva −1..1.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace DriveLab.Core.Telemetry.Effects;

/// <summary>
/// Mistura os efeitos de telemetria: soma (efeito.Compute × efeito.Gain) dos que estão ligados, aplica um
/// ganho mestre e satura em −1..1. O resultado é uma força ADITIVA que convive com o FFB do jogo (não o
/// substitui). Determinístico e puro → host-testável.
/// </summary>
public sealed class TelemetryEffectMixer
{
    public TelemetryEffectMixer(IEnumerable<ITelemetryEffect> effects)
    {
        Effects = effects.ToList();
    }

    /// <summary>Cria o mixer com o conjunto padrão de efeitos (RPM, marcha, ABS, slip).</summary>
    public static TelemetryEffectMixer CreateDefault() => new(new ITelemetryEffect[]
    {
        new RpmRumbleEffect(),
        new GearShiftEffect(),
        new AbsPulseEffect(),
        new SlipEffect(),
    });

    public IReadOnlyList<ITelemetryEffect> Effects { get; }

    /// <summary>Ganho mestre (0..1) sobre a soma dos efeitos.</summary>
    public float MasterGain { get; set; } = 1.0f;

    /// <summary>Soma dos efeitos ligados em −1..1 (após ganhos e saturação).</summary>
    public float Compute(GameTelemetry t, double timeSeconds)
    {
        float sum = 0f;
        foreach (var e in Effects)
            if (e.Enabled)
                sum += e.Gain * e.Compute(t, timeSeconds);
        return Math.Clamp(sum * MasterGain, -1f, 1f);
    }
}
