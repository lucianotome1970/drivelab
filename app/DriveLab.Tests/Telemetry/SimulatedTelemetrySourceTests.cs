// ============================================================================
//  DriveLab
//  SimulatedTelemetrySourceTests.cs — Testes da fonte simulada (varredura de RPM determinística).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Telemetry;

namespace DriveLab.Tests.Telemetry;

public class SimulatedTelemetrySourceTests
{
    [Fact]
    public void Sweep_RpmFollowsClock()
    {
        double now = 0;
        var src = new SimulatedTelemetrySource(() => now, maxRpm: 8000f, sweepSeconds: 4.0);

        Assert.True(src.IsAvailable);

        now = 0;
        Assert.True(src.TryRead(out var t0));
        Assert.True(t0.HasData);
        Assert.Equal(0f, t0.Rpm, 1f);

        now = 2;   // metade da varredura
        src.TryRead(out var tMid);
        Assert.Equal(4000f, tMid.Rpm, 1f);

        now = 4;   // reinicia (4 % 4 = 0)
        src.TryRead(out var tWrap);
        Assert.Equal(0f, tWrap.Rpm, 1f);
    }
}
