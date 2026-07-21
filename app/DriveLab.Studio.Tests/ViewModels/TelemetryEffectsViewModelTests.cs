// ============================================================================
//  DriveLab
//  TelemetryEffectsViewModelTests.cs — Testes do painel dos efeitos (master, por-efeito, readout).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Xunit;
using DriveLab.Core.Telemetry;
using DriveLab.Core.Telemetry.Effects;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Tests.ViewModels;

public class TelemetryEffectsViewModelTests
{
    private sealed class SyncDispatcher : IUiDispatcher
    {
        public void Post(System.Action action) => action();
    }

    private static GameTelemetryService MakeService()
    {
        var svc = new GameTelemetryService(System.Array.Empty<IGameTelemetrySource>(),
            _ => System.Threading.Tasks.Task.CompletedTask, () => 0.0);
        svc.EffectMixer = TelemetryEffectMixer.CreateDefault();
        return svc;
    }

    [Fact]
    public void MirrorsMixer_AndWritesBack()
    {
        var svc = MakeService();
        var vm = new TelemetryEffectsViewModel(svc, new SyncDispatcher());

        Assert.Equal(4, vm.Effects.Count);                       // RPM/Marcha/ABS/Slip
        Assert.True(vm.Enabled);                                 // EffectsEnabled default true

        vm.Enabled = false;
        Assert.False(svc.EffectsEnabled);

        vm.MasterGainPercent = 50;
        Assert.Equal(0.5f, svc.EffectMixer!.MasterGain, 3);

        var first = vm.Effects[0];
        first.Enabled = false;
        first.GainPercent = 120;
        Assert.False(svc.EffectMixer.Effects[0].Enabled);
        Assert.Equal(1.2f, svc.EffectMixer.Effects[0].Gain, 3);
    }

    [Fact]
    public void ForceReadout_UpdatesOnTelemetry()
    {
        var svc = MakeService();
        var vm = new TelemetryEffectsViewModel(svc, new SyncDispatcher());

        // Só o ABS ligado com ganho 1; ABS=1 em t=0 → força 255 → readout 100%.
        foreach (var e in svc.EffectMixer!.Effects) { e.Enabled = e.Name == "ABS"; if (e.Name == "ABS") e.Gain = 1f; }
        svc.SendTelemetryForce = _ => System.Threading.Tasks.Task.CompletedTask;
        svc.ForcedSource = new StubSource(new GameTelemetry { Abs = 1f, HasData = true, MaxRpm = 8000 });

        svc.TickAsync().GetAwaiter().GetResult();
        Assert.Equal(100, vm.ForcePercent);
    }

    private sealed class StubSource : IGameTelemetrySource
    {
        private readonly GameTelemetry _f;
        public StubSource(GameTelemetry f) => _f = f;
        public string Name => "Stub";
        public bool IsAvailable => true;
        public bool TryRead(out GameTelemetry telemetry) { telemetry = _f; return true; }
        public void Dispose() { }
    }
}
