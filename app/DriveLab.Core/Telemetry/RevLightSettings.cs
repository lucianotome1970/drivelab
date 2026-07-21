// ============================================================================
//  DriveLab
//  RevLightSettings.cs — Parâmetros de mapeamento RPM→barra de LEDs (zonas, cores, ponto de shift, blink).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Core.Telemetry;

/// <summary>
/// Configuração dos rev-lights: quantos LEDs formam a barra, em que fração do RPM ela começa a acender,
/// as cores das três zonas (verde/amarelo/vermelho), a cor/piscar do shift e as cores de bandeira.
/// Valores neutros e ajustáveis pelo usuário; o <see cref="RevLightMapper"/> só lê daqui.
/// </summary>
public sealed record RevLightSettings
{
    /// <summary>Número de LEDs na barra de rev (o aro DriveLab tem 8).</summary>
    public int BarLedCount { get; init; } = 8;

    /// <summary>Fração do RPM de shift em que o primeiro LED acende (ex.: 0.80 = barra começa a 80% do shift).</summary>
    public float StartFraction { get; init; } = 0.80f;

    /// <summary>Até que fração da barra é verde (posição do LED, 0..1).</summary>
    public float GreenUpTo { get; init; } = 0.45f;

    /// <summary>Até que fração da barra é amarela; acima disso, vermelho.</summary>
    public float YellowUpTo { get; init; } = 0.78f;

    /// <summary>Fração de <see cref="GameTelemetry.MaxRpm"/> usada como ponto de shift quando o jogo não informa
    /// um <see cref="GameTelemetry.ShiftRpm"/> explícito.</summary>
    public float ShiftFraction { get; init; } = 0.985f;

    /// <summary>Frequência do piscar (Hz) quando o RPM atinge o ponto de shift.</summary>
    public float BlinkHz { get; init; } = 8f;

    public WheelLedColor GreenColor { get; init; } = new(0, 200, 0);
    public WheelLedColor YellowColor { get; init; } = new(255, 120, 0);
    public WheelLedColor RedColor { get; init; } = new(255, 0, 0);
    public WheelLedColor ShiftColor { get; init; } = new(40, 80, 255);

    public WheelLedColor FlagYellow { get; init; } = new(255, 170, 0);
    public WheelLedColor FlagBlue { get; init; } = new(0, 60, 255);
    public WheelLedColor FlagWhite { get; init; } = new(255, 255, 255);
    public WheelLedColor FlagRed { get; init; } = new(255, 0, 0);

    public static readonly RevLightSettings Default = new();
}
