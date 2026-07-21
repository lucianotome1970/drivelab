// ============================================================================
//  DriveLab
//  GameProfileMapTests.cs — Testes do mapa jogo → perfis por módulo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Games;

namespace DriveLab.Tests.Games;

public class GameProfileMapTests
{
    [Fact]
    public void SetAndGet()
    {
        var map = new GameProfileMap();
        map.Set("acc", new ModuleProfiles { Base = "GT3", Wheel = "Rain" });

        var m = map.For("acc");
        Assert.NotNull(m);
        Assert.Equal("GT3", m!.Base);
        Assert.Equal("Rain", m.Wheel);
        Assert.Null(m.Pedals);
    }

    [Fact]
    public void UnknownGame_ReturnsNull()
    {
        Assert.Null(new GameProfileMap().For("rf2"));
    }
}
