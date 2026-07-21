// ============================================================================
//  DriveLab
//  GameCatalog.cs — Catálogo de sims conhecidos (id, nome, nomes de processo) para detecção do jogo rodando.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.Generic;
using System.Linq;

namespace DriveLab.Core.Games;

/// <summary>Um sim conhecido: identificador estável, nome de exibição e os nomes de processo (executável, sem
/// extensão) que indicam que ele está rodando.</summary>
public sealed record KnownGame(string Id, string DisplayName, string[] ProcessNames);

/// <summary>Catálogo dos sims suportados para auto-perfil. Nomes de processo do Windows (sem ".exe").</summary>
public static class GameCatalog
{
    public static readonly IReadOnlyList<KnownGame> All = new[]
    {
        new KnownGame("acc",     "Assetto Corsa Competizione", new[] { "acc" }),
        new KnownGame("ac",      "Assetto Corsa",              new[] { "acs", "AssettoCorsa" }),
        new KnownGame("acevo",   "Assetto Corsa EVO",          new[] { "AssettoCorsaEVO", "acevo" }),
        new KnownGame("iracing", "iRacing",                    new[] { "iRacingSim64DX11", "iRacingSim64DX12", "iRacingSim" }),
        new KnownGame("rf2",     "rFactor 2",                  new[] { "rFactor2" }),
        new KnownGame("lmu",     "Le Mans Ultimate",           new[] { "Le Mans Ultimate" }),
        new KnownGame("ams2",    "Automobilista 2",            new[] { "AMS2AVX", "AMS2" }),
    };

    public static KnownGame? ById(string id) => All.FirstOrDefault(g => g.Id == id);
}
