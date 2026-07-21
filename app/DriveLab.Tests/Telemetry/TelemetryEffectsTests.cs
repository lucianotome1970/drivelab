// ============================================================================
//  DriveLab
//  TelemetryEffectsTests.cs — Testes dos efeitos de FFB por telemetria (RPM, marcha, ABS, slip) + mixer.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using DriveLab.Core.Telemetry;
using DriveLab.Core.Telemetry.Effects;

namespace DriveLab.Tests.Telemetry;

public class TelemetryEffectsTests
{
    private static GameTelemetry Frame(float rpm = 0, float maxRpm = 8000, int gear = 1,
        float abs = 0, float slip = 0, bool hasData = true) =>
        new() { Rpm = rpm, MaxRpm = maxRpm, Gear = gear, Abs = abs, Slip = slip, HasData = hasData };

    // ---------------- RPM rumble ----------------
    [Fact]
    public void RpmRumble_SilentBelowStart_AmplitudeGrowsWithRpm()
    {
        var e = new RpmRumbleEffect();
        Assert.Equal(0f, e.Compute(Frame(rpm: 1600), 0.0));   // 0.2*max < start 0.30 → silêncio

        // frac 0.65 → amp = (0.65-0.30)/0.70 = 0.5; escolhe t no pico (sin=1).
        float hz = e.MinHz + (e.MaxHz - e.MinHz) * 0.65f;
        double tPeak = 1.0 / (4 * hz);                        // sin(2π·hz·t)=sin(π/2)=1
        float outp = e.Compute(Frame(rpm: 0.65f * 8000), tPeak);
        Assert.InRange(outp, 0.49f, 0.51f);
    }

    [Fact]
    public void RpmRumble_NoData_Zero() =>
        Assert.Equal(0f, new RpmRumbleEffect().Compute(Frame(rpm: 8000, hasData: false), 0.1));

    // ---------------- Gear shift ----------------
    [Fact]
    public void GearShift_FiresOnChange_ThenDecaysToZero()
    {
        var e = new GearShiftEffect();
        Assert.Equal(0f, e.Compute(Frame(gear: 3), 0.0));      // 1ª leitura arma a "última marcha", não dispara

        Assert.Equal(0f, e.Compute(Frame(gear: 4), 10.0));     // borda: arma o pulso; em dt=0 sin(0)=0

        double tPeak = 10.0 + 1.0 / (4 * e.PulseHz);           // pico do pulso
        Assert.True(e.Compute(Frame(gear: 4), tPeak) > 0.85f);

        Assert.Equal(0f, e.Compute(Frame(gear: 4), 10.0 + e.DurationSeconds + 0.01)); // após a duração → 0
    }

    // ---------------- ABS pulse ----------------
    [Fact]
    public void AbsPulse_SquareScaledByIntensity()
    {
        var e = new AbsPulseEffect();
        Assert.Equal(0f, e.Compute(Frame(abs: 0f), 0.0));      // sem ABS → 0

        Assert.Equal(0.5f, e.Compute(Frame(abs: 0.5f), 0.0), 3);          // fase 0 → +1 → 0.5
        double tNeg = 0.5 / e.PulseHz;                                     // fase 0.5 → -1
        Assert.Equal(-0.5f, e.Compute(Frame(abs: 0.5f), tNeg), 3);
    }

    // ---------------- Slip ----------------
    [Fact]
    public void Slip_SilentBelowThreshold_ScalesAbove()
    {
        var e = new SlipEffect();
        Assert.Equal(0f, e.Compute(Frame(slip: 0.1f), 0.0));   // < threshold 0.15

        // slip 0.575 → amp = (0.575-0.15)/(1.0-0.15) = 0.5; pico da senoide.
        double tPeak = 1.0 / (4 * e.VibHz);
        Assert.InRange(e.Compute(Frame(slip: 0.575f), tPeak), 0.49f, 0.51f);
    }

    // ---------------- Mixer ----------------
    [Fact]
    public void Mixer_Default_HasFourEffects_AndAllDisabledIsZero()
    {
        var mix = TelemetryEffectMixer.CreateDefault();
        Assert.Equal(4, mix.Effects.Count);
        foreach (var e in mix.Effects) e.Enabled = false;
        Assert.Equal(0f, mix.Compute(Frame(rpm: 8000, abs: 1, slip: 1), 0.0));
    }

    [Fact]
    public void Mixer_SumsEnabledEffects_AndClamps()
    {
        var abs = new AbsPulseEffect { Gain = 0.6f };
        var mix = new TelemetryEffectMixer(new ITelemetryEffect[] { abs });
        Assert.Equal(0.6f, mix.Compute(Frame(abs: 1f), 0.0), 3);   // 1.0 (quadrada) * 0.6

        abs.Gain = 2.0f;                                            // força estouro → satura em 1.0
        Assert.Equal(1.0f, mix.Compute(Frame(abs: 1f), 0.0), 3);
    }
}
