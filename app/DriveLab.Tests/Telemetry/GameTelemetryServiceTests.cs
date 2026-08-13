// ============================================================================
//  DriveLab
//  GameTelemetryServiceTests.cs — Testes da orquestração (seleção de fonte, BuildFrame, TickAsync, cores de botão).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Telemetry;
using DriveLab.Core.Telemetry.Effects;

namespace DriveLab.Tests.Telemetry;

public class GameTelemetryServiceTests
{
    private sealed class FakeSource : IGameTelemetrySource
    {
        public FakeSource(string name, bool available, GameTelemetry frame)
        { Name = name; IsAvailable = available; _frame = frame; }
        private readonly GameTelemetry _frame;
        public string Name { get; }
        public bool IsAvailable { get; set; }
        public bool TryRead(out GameTelemetry telemetry) { telemetry = _frame; return IsAvailable; }
        public void Dispose() { }
    }

    private static readonly WheelLedColor Off = new(0, 0, 0);
    private static GameTelemetry Redline() =>
        new() { Rpm = 8000, MaxRpm = 8000, HasData = true };

    private static GameTelemetryService Service(out List<WheelLedReport> sent, params IGameTelemetrySource[] sources)
    {
        var captured = new List<WheelLedReport>();
        sent = captured;
        return new GameTelemetryService(sources, r => { captured.Add(r); return Task.CompletedTask; }, () => 0.0);
    }

    [Fact]
    public void SelectSource_FirstAvailableWins()
    {
        var a = new FakeSource("A", available: false, Redline());
        var b = new FakeSource("B", available: true, Redline());
        var svc = Service(out _, a, b);

        Assert.Equal("B", svc.SelectSource()?.Name);
    }

    [Fact]
    public void ForcedSource_OverridesRealSources()
    {
        var real = new FakeSource("ACC", available: true, Redline());
        var svc = Service(out _, real);
        svc.ForcedSource = new FakeSource("Simulado", available: true, Redline());

        Assert.Equal("Simulado", svc.SelectSource()?.Name);
    }

    [Fact]
    public void BuildFrame_NoSource_BarOff()
    {
        var svc = Service(out _);   // sem fontes
        var report = svc.BuildFrame(0.0, out var t);

        Assert.False(t.HasData);
        Assert.Null(svc.ActiveSourceName);
        // barra (10-17) apagada
        for (int i = RevLightFrame.ButtonLedCount; i < report.Leds.Count; i++)
            Assert.Equal(Off, report.Leds[i]);
    }

    [Fact]
    public void BuildFrame_KeepsButtonColorsUnderBar()
    {
        var svc = Service(out _, new FakeSource("ACC", true, Redline()));
        var buttons = Enumerable.Range(1, 10).Select(i => new WheelLedColor((byte)i, 0, 0)).ToArray();
        svc.SetButtonColors(buttons);

        var report = svc.BuildFrame(0.0, out _);

        Assert.Equal(new WheelLedColor(1, 0, 0), report.Leds[0]);   // botão preservado
        Assert.Equal(new WheelLedColor(10, 0, 0), report.Leds[9]);
        Assert.Equal(svc.Settings.ShiftColor, report.Leds[10]);     // barra no shift (redline, t=0 → aceso)
        Assert.Equal("ACC", svc.ActiveSourceName);
    }

    [Fact]
    public async Task TickAsync_WithMixer_SendsTelemetryForce()
    {
        // Fonte com ABS=1 → AbsPulseEffect (gain 1) em t=0 → +1 → força 255.
        var frame = new GameTelemetry { Rpm = 3000, MaxRpm = 8000, Abs = 1f, HasData = true };
        var svc = Service(out _, new FakeSource("ACC", true, frame));
        svc.EffectMixer = new TelemetryEffectMixer(new ITelemetryEffect[] { new AbsPulseEffect { Gain = 1f } });
        float? sentForce = null;
        svc.SendTelemetryForce = f => { sentForce = f; return Task.CompletedTask; };

        await svc.TickAsync();

        Assert.True(sentForce.HasValue);
        Assert.Equal(255f, sentForce!.Value, 1f);
        Assert.Equal(255f, svc.LastEffectForce, 1f);
    }

    [Fact]
    public async Task TickAsync_EffectsDisabled_DoesNotSendForce()
    {
        var svc = Service(out _, new FakeSource("ACC", true, Redline()));
        svc.EffectMixer = TelemetryEffectMixer.CreateDefault();
        svc.EffectsEnabled = false;
        bool sent = false;
        svc.SendTelemetryForce = _ => { sent = true; return Task.CompletedTask; };

        await svc.TickAsync();

        Assert.False(sent);
    }

    [Fact]
    public async Task TickAsync_SendsFrameAndRaisesEvent()
    {
        var svc = Service(out var sent, new FakeSource("ACC", true, Redline()));
        GameTelemetry? seen = null;
        svc.TelemetryUpdated += (_, t) => seen = t;

        var result = await svc.TickAsync();

        Assert.Single(sent);
        Assert.Equal(18, sent[0].Leds.Count);
        Assert.True(result.HasData);
        Assert.True(seen.HasValue);
        Assert.Equal(8000, svc.LastTelemetry.Rpm);
    }
}
