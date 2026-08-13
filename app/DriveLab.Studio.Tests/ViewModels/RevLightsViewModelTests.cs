// ============================================================================
//  DriveLab
//  RevLightsViewModelTests.cs — Testes do painel de rev-lights (ativar/parar, modo teste, readout ao vivo).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Xunit;
using DriveLab.Core.Telemetry;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Tests.ViewModels;

public class RevLightsViewModelTests
{
    private sealed class SyncDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();   // executa na hora (teste determinístico)
    }

    private static GameTelemetryService MakeService(Func<double> clock)
    {
        return new GameTelemetryService(
            Array.Empty<IGameTelemetrySource>(),          // sem fontes reais → só a forçada (teste)
            _ => Task.CompletedTask,
            clock);
    }

    [Fact]
    public void TestMode_SetsAndClearsForcedSource()
    {
        var svc = MakeService(() => 0);
        var vm = new RevLightsViewModel(svc, new SyncDispatcher(), () => 0, () => { });

        vm.TestMode = true;
        Assert.NotNull(svc.ForcedSource);
        Assert.Equal("Simulado", svc.ForcedSource!.Name);

        vm.TestMode = false;
        Assert.Null(svc.ForcedSource);
    }

    [Fact]
    public void Enable_StartsLoop_Disable_StopsAndRestores()
    {
        var svc = MakeService(() => 0);
        bool restored = false;
        var vm = new RevLightsViewModel(svc, new SyncDispatcher(), () => 0, () => restored = true);

        vm.Enabled = true;
        Assert.True(svc.IsRunning);

        vm.Enabled = false;
        Assert.False(svc.IsRunning);
        Assert.True(restored);
        Assert.Equal("—", vm.ActiveSource);
    }

    [Fact]
    public async Task Tick_UpdatesLiveReadout()
    {
        double now = 0;
        var svc = MakeService(() => now);
        var vm = new RevLightsViewModel(svc, new SyncDispatcher(), () => now, () => { })
        {
            TestMode = true,   // fonte simulada (maxRpm 8000, varredura 4s)
        };

        now = 2;   // metade da varredura → RPM ~4000
        await svc.TickAsync();

        Assert.True(vm.HasData);
        Assert.Equal("Simulado", vm.ActiveSource);
        Assert.InRange(vm.Rpm, 3900, 4100);
        Assert.Equal(8000, vm.MaxRpm);
    }
}
