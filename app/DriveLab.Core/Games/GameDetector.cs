// ============================================================================
//  DriveLab
//  GameDetector.cs — Detecta qual sim conhecido está rodando, casando processos ativos com o catálogo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;

namespace DriveLab.Core.Games;

/// <summary>
/// Detecta o sim rodando comparando os nomes de processo ativos com o <see cref="GameCatalog"/>. O provedor de
/// processos é injetado (no app: <c>Process.GetProcesses().Select(p =&gt; p.ProcessName)</c>), então a lógica é
/// pura e host-testável — sem depender de haver jogo/SO real.
/// </summary>
public sealed class GameDetector
{
    private readonly Func<IReadOnlyList<string>> _runningProcessNames;
    private readonly IReadOnlyList<KnownGame> _catalog;

    public GameDetector(Func<IReadOnlyList<string>> runningProcessNames, IReadOnlyList<KnownGame>? catalog = null)
    {
        _runningProcessNames = runningProcessNames;
        _catalog = catalog ?? GameCatalog.All;
    }

    /// <summary>Primeiro sim do catálogo cujo processo está ativo, ou null se nenhum. Comparação sem caixa.</summary>
    public KnownGame? Detect()
    {
        var running = new HashSet<string>(
            _runningProcessNames().Select(n => n.ToLowerInvariant()),
            StringComparer.Ordinal);

        foreach (var game in _catalog)
            if (game.ProcessNames.Any(p => running.Contains(p.ToLowerInvariant())))
                return game;
        return null;
    }
}
