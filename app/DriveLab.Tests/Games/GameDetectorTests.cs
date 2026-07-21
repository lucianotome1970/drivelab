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
}
