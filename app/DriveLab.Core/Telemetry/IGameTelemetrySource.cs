// ============================================================================
//  DriveLab
//  IGameTelemetrySource.cs — Contrato de uma fonte de telemetria de jogo (shared memory / UDP / simulada).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Telemetry;

/// <summary>
/// Uma fonte de telemetria de jogo. Modelo pull (poll) — o serviço chama <see cref="TryRead"/> num timer.
/// Implementações reais (ACC/iRacing/rF2) leem shared memory e só ficam <see cref="IsAvailable"/> quando o
/// jogo está rodando; a <see cref="SimulatedTelemetrySource"/> serve para dev/teste sem jogo e sem bancada.
/// </summary>
public interface IGameTelemetrySource : IDisposable
{
    /// <summary>Nome curto do jogo/fonte (ex.: "ACC", "iRacing", "Simulado").</summary>
    string Name { get; }

    /// <summary>True quando a fonte está disponível (jogo rodando / shared memory montada).</summary>
    bool IsAvailable { get; }

    /// <summary>Tenta ler o quadro atual. Retorna false (e telemetria vazia) se indisponível.</summary>
    bool TryRead(out GameTelemetry telemetry);
}
