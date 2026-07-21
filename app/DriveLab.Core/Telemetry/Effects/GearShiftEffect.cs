// ============================================================================
//  DriveLab
//  GearShiftEffect.cs — Chute breve e decaído a cada troca de marcha (detecta a borda de mudança).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;

namespace DriveLab.Core.Telemetry.Effects;

/// <summary>
/// Solavanco de marcha: ao detectar a troca (marcha diferente da anterior), dispara um pulso senoidal que
/// decai em <see cref="DurationSeconds"/>. Com estado (guarda a última marcha e o instante da troca), mas o
/// cálculo continua determinístico dado (telemetria, tempo). Ignora idas de/para neutro por padrão? Não —
/// qualquer mudança conta; o usuário regula pelo Gain.
/// </summary>
public sealed class GearShiftEffect : ITelemetryEffect
{
    public string Name => "Marcha";
    public bool Enabled { get; set; } = true;
    public float Gain { get; set; } = 0.5f;

    /// <summary>Duração do pulso (s).</summary>
    public float DurationSeconds { get; set; } = 0.12f;

    /// <summary>Frequência do pulso (Hz).</summary>
    public float PulseHz { get; set; } = 30f;

    private int _lastGear = int.MinValue;
    private double _shiftTime = double.NegativeInfinity;

    public float Compute(GameTelemetry t, double timeSeconds)
    {
        if (!t.HasData) { _lastGear = int.MinValue; return 0f; }

        if (_lastGear != int.MinValue && t.Gear != _lastGear)
            _shiftTime = timeSeconds;         // borda de troca → arma o pulso
        _lastGear = t.Gear;

        double dt = timeSeconds - _shiftTime;
        if (dt < 0 || dt > DurationSeconds) return 0f;

        float env = 1f - (float)(dt / DurationSeconds);          // envelope decrescente
        return env * (float)Math.Sin(2 * Math.PI * PulseHz * dt);
    }
}
