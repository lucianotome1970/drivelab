// ============================================================================
//  DriveLab
//  AutoProfileServiceTests.cs — Testes do auto-perfil: detecta jogo, aplica perfis casados, troca única.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.Generic;
using Xunit;
using DriveLab.Core.Games;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.Tests.Services;

public class AutoProfileServiceTests
{
    private sealed class NoopDispatcher : IUiDispatcher { public void Post(System.Action a) => a(); }

    private static (AutoProfileService svc, List<string> applied, GameProfileMap map) Build(
        System.Func<IReadOnlyList<string>> processes)
    {
        var applied = new List<string>();
        var map = new GameProfileMap();
        var detector = new GameDetector(processes);
        var svc = new AutoProfileService(detector, () => map, new NoopDispatcher(),
            b => applied.Add("base:" + b),
            w => applied.Add("wheel:" + w),
            p => applied.Add("pedals:" + p),
            h => applied.Add("hb:" + h));
        return (svc, applied, map);
    }

    [Fact]
    public void Disabled_DoesNothing()
    {
        var (svc, applied, map) = Build(() => new[] { "acc" });
        map.Set("acc", new ModuleProfiles { Base = "GT3" });
        svc.Tick();
        Assert.Empty(applied);
        Assert.Null(svc.CurrentGame);
    }

    [Fact]
    public void OnGameDetected_AppliesMappedProfiles_OnlySetModules()
    {
        var (svc, applied, map) = Build(() => new[] { "acc" });
        map.Set("acc", new ModuleProfiles { Base = "GT3", Wheel = "Rain" });   // pedais/freio não mapeados
        svc.Enabled = true;

        svc.Tick();

        Assert.Equal("acc", svc.CurrentGame?.Id);
        Assert.Contains("base:GT3", applied);
        Assert.Contains("wheel:Rain", applied);
        Assert.DoesNotContain(applied, a => a.StartsWith("pedals:"));
        Assert.DoesNotContain(applied, a => a.StartsWith("hb:"));
    }

    [Fact]
    public void SameGameTwice_AppliesOnce()
    {
        var (svc, applied, map) = Build(() => new[] { "acc" });
        map.Set("acc", new ModuleProfiles { Base = "GT3" });
        svc.Enabled = true;

        svc.Tick();
        svc.Tick();   // mesmo jogo → não reaplica

        Assert.Single(applied);
    }

    [Fact]
    public void UnsavedChanges_SkipsSwitch_InsteadOfClobbering()
    {
        var (svc, applied, map) = Build(() => new[] { "acc" });
        map.Set("acc", new ModuleProfiles { Base = "GT3" });
        svc.Enabled = true;
        svc.HasUnsavedChanges = () => true;   // usuário está no meio de um ajuste

        svc.Tick();

        Assert.Empty(applied);                 // NÃO aplicou por cima
        Assert.True(svc.LastSwitchSkipped);
        Assert.Equal("acc", svc.CurrentGame?.Id);   // mas detectou o jogo (UI mostra)
    }

    [Fact]
    public void NoUnsavedChanges_AppliesNormally()
    {
        var (svc, applied, map) = Build(() => new[] { "acc" });
        map.Set("acc", new ModuleProfiles { Base = "GT3" });
        svc.Enabled = true;
        svc.HasUnsavedChanges = () => false;

        svc.Tick();

        Assert.Contains("base:GT3", applied);
        Assert.False(svc.LastSwitchSkipped);
    }

    [Fact]
    public void GameClosed_ClearsCurrentAndDoesNotApply()
    {
        var running = new List<string> { "acc" };
        var (svc, applied, map) = Build(() => running);
        map.Set("acc", new ModuleProfiles { Base = "GT3" });
        svc.Enabled = true;

        svc.Tick();                 // detecta acc
        running.Clear();            // jogo fechou
        KnownGame? seen = null; bool fired = false;
        svc.GameChanged += (_, g) => { seen = g; fired = true; };
        svc.Tick();

        Assert.Null(svc.CurrentGame);
        Assert.True(fired);
        Assert.Null(seen);
        Assert.Single(applied);     // só a aplicação inicial
    }
}
