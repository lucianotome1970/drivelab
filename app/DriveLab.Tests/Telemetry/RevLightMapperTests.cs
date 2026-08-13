// ============================================================================
//  DriveLab
//  RevLightMapperTests.cs — Testes do mapeamento RPM→barra de rev-lights (zonas, shift/blink, bandeiras).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Telemetry;

namespace DriveLab.Tests.Telemetry;

public class RevLightMapperTests
{
    private static readonly RevLightSettings S = RevLightSettings.Default;   // 8 LEDs, shift 0.985, start 0.80
    private static readonly WheelLedColor Off = new(0, 0, 0);

    private static GameTelemetry Frame(float rpm, float maxRpm = 8000f, float shiftRpm = 0f,
        GameFlag flag = GameFlag.None, bool hasData = true) =>
        new() { Rpm = rpm, MaxRpm = maxRpm, ShiftRpm = shiftRpm, Flag = flag, HasData = hasData };

    [Fact]
    public void NoData_AllOff()
    {
        var bar = RevLightMapper.Compute(Frame(7000, hasData: false), S, 0);
        Assert.Equal(8, bar.Length);
        Assert.All(bar, c => Assert.Equal(Off, c));
    }

    [Fact]
    public void BelowStart_AllOff()
    {
        // shiftRpm = 8000*0.985 = 7880; start = 7880*0.80 = 6304. 6000 < start → nada aceso.
        var bar = RevLightMapper.Compute(Frame(6000), S, 0);
        Assert.All(bar, c => Assert.Equal(Off, c));
    }

    [Fact]
    public void RisingRpm_FillsMoreLeds()
    {
        int Lit(float rpm) => RevLightMapper.Compute(Frame(rpm), S, 0).Count(c => c != Off);
        Assert.True(Lit(6304) < Lit(7092));   // início vs meio
        Assert.True(Lit(7092) < Lit(7850));   // meio vs quase-shift
    }

    [Fact]
    public void NearShift_ZoneColorsGreenYellowRed()
    {
        // Rpm 7850 → frac ≈ 0.98 → 7 LEDs acesos (índices 0..6).
        var bar = RevLightMapper.Compute(Frame(7850), S, 0);
        Assert.Equal(S.GreenColor, bar[0]);    // pos 0.06 ≤ 0.45
        Assert.Equal(S.YellowColor, bar[4]);   // pos 0.56 ≤ 0.78
        Assert.Equal(S.RedColor, bar[6]);      // pos 0.81 > 0.78
        Assert.Equal(Off, bar[7]);             // ainda não aceso
    }

    [Fact]
    public void AtShift_Blinks()
    {
        var on = RevLightMapper.Compute(Frame(8000), S, 0.0);       // (long)(0*8)=0 par → ligado
        var off = RevLightMapper.Compute(Frame(8000), S, 0.125);    // (long)(0.125*8)=1 ímpar → apagado
        Assert.All(on, c => Assert.Equal(S.ShiftColor, c));
        Assert.All(off, c => Assert.Equal(Off, c));
    }

    [Fact]
    public void ExplicitShiftRpm_IsHonored()
    {
        // ShiftRpm explícito 7000 (< 8000*0.985); Rpm 7000 já dispara o blink.
        var bar = RevLightMapper.Compute(Frame(7000, shiftRpm: 7000), S, 0.0);
        Assert.All(bar, c => Assert.Equal(S.ShiftColor, c));
    }

    [Fact]
    public void Flag_OverridesEvenAtRedline()
    {
        var yellow = RevLightMapper.Compute(Frame(8000, flag: GameFlag.Yellow), S, 0.0);
        Assert.All(yellow, c => Assert.Equal(S.FlagYellow, c));

        var blue = RevLightMapper.Compute(Frame(3000, flag: GameFlag.Blue), S, 0.0);
        Assert.All(blue, c => Assert.Equal(S.FlagBlue, c));
    }

    [Fact]
    public void GreenFlag_DoesNotOverride()
    {
        // Bandeira verde não pinta a barra — RPM baixo continua apagado.
        var bar = RevLightMapper.Compute(Frame(6000, flag: GameFlag.Green), S, 0);
        Assert.All(bar, c => Assert.Equal(Off, c));
    }
}
