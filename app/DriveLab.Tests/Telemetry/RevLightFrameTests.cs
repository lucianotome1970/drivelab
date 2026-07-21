// ============================================================================
//  DriveLab
//  RevLightFrameTests.cs — Testes da junção botões(0-9) + barra(10-17) no WheelLedReport.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Telemetry;

namespace DriveLab.Tests.Telemetry;

public class RevLightFrameTests
{
    private static readonly WheelLedColor Off = new(0, 0, 0);

    [Fact]
    public void Build_PlacesButtonsThenBar()
    {
        var buttons = Enumerable.Range(1, 10).Select(i => new WheelLedColor((byte)i, 0, 0)).ToArray();
        var bar = Enumerable.Range(1, 8).Select(i => new WheelLedColor(0, (byte)i, 0)).ToArray();

        var report = RevLightFrame.Build(buttons, bar, brightness: 200);

        Assert.Equal(18, report.Leds.Count);
        Assert.Equal(200, report.Brightness);
        Assert.Equal(new WheelLedColor(1, 0, 0), report.Leds[0]);     // primeiro botão
        Assert.Equal(new WheelLedColor(10, 0, 0), report.Leds[9]);    // último botão
        Assert.Equal(new WheelLedColor(0, 1, 0), report.Leds[10]);    // primeiro LED da barra
        Assert.Equal(new WheelLedColor(0, 8, 0), report.Leds[17]);    // último LED da barra
    }

    [Fact]
    public void Build_PadsMissingButtonsWithOff()
    {
        var buttons = new[] { new WheelLedColor(9, 9, 9) };            // só 1 botão informado
        var bar = new WheelLedColor[8];                                // barra apagada

        var report = RevLightFrame.Build(buttons, bar, brightness: 100);

        Assert.Equal(18, report.Leds.Count);
        Assert.Equal(new WheelLedColor(9, 9, 9), report.Leds[0]);
        Assert.Equal(Off, report.Leds[1]);                            // botões 1-9 → apagados
        Assert.Equal(Off, report.Leds[9]);
    }
}
