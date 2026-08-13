// ============================================================================
//  DriveLab
//  GameTelemetry.cs — Telemetria de jogo normalizada (RPM, marcha, velocidade, bandeira) independente de simulador.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Telemetry;

/// <summary>Bandeira de corrida normalizada. Cada fonte de jogo mapeia o seu próprio enum para este.</summary>
public enum GameFlag
{
    None = 0,
    Green,
    Yellow,
    Blue,
    White,
    Red,
    Checkered,
    Black,
}

/// <summary>
/// Telemetria de um jogo, normalizada e independente de fonte (ACC, iRacing, AC, rF2…).
/// Só carrega o mínimo necessário para os rev-lights/bandeiras do aro; a lógica de cor fica no
/// <see cref="RevLightMapper"/>. Valores em unidades absolutas (RPM, km/h) — o mapeamento decide frações.
/// </summary>
public readonly record struct GameTelemetry
{
    /// <summary>RPM atual do motor.</summary>
    public float Rpm { get; init; }

    /// <summary>RPM máximo (redline) do carro. Usado para derivar o ponto de shift quando o jogo não o informa.</summary>
    public float MaxRpm { get; init; }

    /// <summary>RPM em que o shift-light pisca, quando o jogo informa explicitamente (ex.: iRacing). 0 = desconhecido
    /// (o mapeador deriva de <see cref="MaxRpm"/>).</summary>
    public float ShiftRpm { get; init; }

    /// <summary>Marcha: -1 = ré, 0 = neutro, 1..n = marchas.</summary>
    public int Gear { get; init; }

    /// <summary>Velocidade em km/h.</summary>
    public float SpeedKmh { get; init; }

    /// <summary>Bandeira ativa.</summary>
    public GameFlag Flag { get; init; }

    /// <summary>Limitador de pit engatado.</summary>
    public bool PitLimiter { get; init; }

    /// <summary>Intensidade do ABS atuando (0 = inativo, ~1 = cortando forte). Usado pelo efeito de ABS pulsante.</summary>
    public float Abs { get; init; }

    /// <summary>Escorregamento das rodas (0 = aderência total; cresce ao perder aderência). Máx. das 4 rodas.
    /// Usado pelo efeito de slip.</summary>
    public float Slip { get; init; }

    /// <summary>Quadro válido (carro na pista, dados frescos). Fontes devem zerar isto no menu/replay parado.</summary>
    public bool HasData { get; init; }
}
