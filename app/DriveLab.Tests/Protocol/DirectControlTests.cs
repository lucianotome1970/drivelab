// ============================================================================
//  DriveLab
//  DirectControlTests.cs — Testes de round-trip do DirectControl.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Tests.Protocol;

public class DirectControlTests
{
    [Fact]
    public void ToBytes_Has_ReportSize_Length()
    {
        Assert.Equal(ReportConstants.ReportSize, new DirectControl().ToBytes().Length);
    }

    [Fact]
    public void ToBytes_Then_Parse_RoundTrips()
    {
        var control = new DirectControl
        {
            SpringForce = -3000,
            ConstantForce = 5000,
            PeriodicForce = -10000,
            DamperForce = 2500,
            ForceDrop = 40,
        };

        var parsed = DirectControl.Parse(control.ToBytes());

        Assert.Equal(control.SpringForce, parsed.SpringForce);
        Assert.Equal(control.ConstantForce, parsed.ConstantForce);
        Assert.Equal(control.PeriodicForce, parsed.PeriodicForce);
        Assert.Equal(control.DamperForce, parsed.DamperForce);
        Assert.Equal(control.ForceDrop, parsed.ForceDrop);
    }
}
