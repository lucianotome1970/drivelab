// ============================================================================
//  DriveLab
//  WheelLedReportTests.cs — Testes de round-trip do WheelLedReport (cores RGB do rim).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Tests.Protocol;

public class WheelLedReportTests
{
    [Fact]
    public void ToBytes_Has_ReportSize_Length()
    {
        var report = new WheelLedReport(200, new[] { new WheelLedColor(1, 2, 3) });
        Assert.Equal(ReportConstants.ReportSize, report.ToBytes().Length);
    }

    [Fact]
    public void ToBytes_Then_Parse_RoundTrips()
    {
        var leds = new[]
        {
            new WheelLedColor(255, 0, 0),
            new WheelLedColor(0, 255, 0),
            new WheelLedColor(10, 20, 30),
        };
        var report = new WheelLedReport(128, leds);

        var parsed = WheelLedReport.Parse(report.ToBytes());

        Assert.Equal(128, parsed.Brightness);
        Assert.Equal(leds, parsed.Leds);
    }

    [Fact]
    public void Rejects_More_Than_MaxLeds()
    {
        var tooMany = new WheelLedColor[WheelLedReport.MaxLeds + 1];
        Assert.Throws<ArgumentException>(() => new WheelLedReport(255, tooMany));
    }
}
