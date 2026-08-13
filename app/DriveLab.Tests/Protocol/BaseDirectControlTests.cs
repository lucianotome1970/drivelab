// ============================================================================
//  DriveLab
//  BaseDirectControlTests.cs — Testes de round-trip do BaseDirectControl.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Tests.Protocol;

public class BaseDirectControlTests
{
    [Fact]
    public void ToBytes_Has_ReportSize_Length()
    {
        Assert.Equal(ReportConstants.ReportSize, new BaseDirectControl().ToBytes().Length);
    }

    [Fact]
    public void ToBytes_Then_Parse_RoundTrips()
    {
        var control = new BaseDirectControl
        {
            SpringForce = -3000,
            ConstantForce = 5000,
            PeriodicForce = -10000,
            DamperForce = 2500,
            ForceDrop = 40,
            TelemetryForce = -200,
        };

        var parsed = BaseDirectControl.Parse(control.ToBytes());

        Assert.Equal(control.SpringForce, parsed.SpringForce);
        Assert.Equal(control.ConstantForce, parsed.ConstantForce);
        Assert.Equal(control.PeriodicForce, parsed.PeriodicForce);
        Assert.Equal(control.DamperForce, parsed.DamperForce);
        Assert.Equal(control.ForceDrop, parsed.ForceDrop);
        Assert.Equal(control.TelemetryForce, parsed.TelemetryForce);
    }

    [Fact]
    public void TelemetryForce_At_Offset9_MatchesFirmwareRead()
    {
        // O firmware lê o int16 em buf[10..11] (report id em buf[0] + payload[9..10]). Confere o byte-offset 9.
        var bytes = new BaseDirectControl { TelemetryForce = 255 }.ToBytes();
        Assert.Equal(255, System.BitConverter.ToInt16(bytes, 9));
    }
}
