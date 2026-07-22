// ============================================================================
//  DriveLab
//  GameDetectorTests.cs — Testes da detecção de jogo por nome de processo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.Generic;
using DriveLab.Core.Games;

namespace DriveLab.Tests.Games;

public class GameDetectorTests
{
    private static GameDetector With(params string[] processes) =>
        new(() => (IReadOnlyList<string>)processes);

    [Fact]
    public void Detects_Acc_ByProcess()
    {
        var g = With("explorer", "acc", "steam").Detect();
        Assert.NotNull(g);
        Assert.Equal("acc", g!.Id);
    }

    [Fact]
    public void CaseInsensitive()
    {
        Assert.Equal("acc", With("ACC").Detect()?.Id);
    }

    [Fact]
    public void Detects_iRacing()
    {
        Assert.Equal("iracing", With("iRacingSim64DX11").Detect()?.Id);
    }

    [Fact]
    public void NoGame_ReturnsNull()
    {
        Assert.Null(With("notepad", "chrome").Detect());
    }

    // ---- Jogos customizados (fora do catálogo embutido) ----

    [Fact]
    public void WithCustom_AddsUserGame_AndIgnoresIncomplete()
    {
        var catalog = GameCatalog.WithCustom(new[]
        {
            new CustomGame { Id = "mysim", DisplayName = "Meu Sim", ProcessName = "mysim64" },
            new CustomGame { Id = "vazio", DisplayName = "Sem exe", ProcessName = "" },   // ignorado
        });

        Assert.Contains(catalog, g => g.Id == "mysim" && g.ProcessNames[0] == "mysim64");
        Assert.DoesNotContain(catalog, g => g.Id == "vazio");
        Assert.Contains(catalog, g => g.Id == "acc");   // embutidos continuam
    }

    [Fact]
    public void Detects_CustomGame_FromDynamicCatalog()
    {
        var custom = new List<CustomGame>();
        // catálogo resolvido a cada Detect() → adicionar em runtime passa a valer sem recriar o detector
        var detector = new GameDetector(() => (IReadOnlyList<string>)new[] { "mysim64" },
            () => GameCatalog.WithCustom(custom));

        Assert.Null(detector.Detect());

        custom.Add(new CustomGame { Id = "mysim", DisplayName = "Meu Sim", ProcessName = "mysim64" });

        Assert.Equal("mysim", detector.Detect()?.Id);
    }
}
