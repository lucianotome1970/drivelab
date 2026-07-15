// ============================================================================
//  DriveLab
//  SimulatorStreamingTests.cs — Testes de streaming (start/stop) do SimulatorTransport.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Simulator;

namespace DriveLab.Tests.Simulator;

public class SimulatorStreamingTests
{
    [Fact]
    public async Task StartStreaming_Emits_States_Over_Time()
    {
        var transport = new SimulatorTransport();
        await transport.ConnectAsync();
        var count = 0;
        transport.StateReceived += (_, _) => Interlocked.Increment(ref count);

        transport.StartStreaming(hz: 100);
        await Task.Delay(150);
        transport.StopStreaming();

        Assert.True(count >= 5, $"expected several states, got {count}");
    }

    [Fact]
    public async Task StopStreaming_Halts_Emissions()
    {
        var transport = new SimulatorTransport();
        await transport.ConnectAsync();
        transport.StartStreaming(hz: 100);
        await Task.Delay(50);
        transport.StopStreaming();

        var countAfterStop = 0;
        transport.StateReceived += (_, _) => Interlocked.Increment(ref countAfterStop);
        await Task.Delay(100);

        Assert.Equal(0, countAfterStop);
    }
}
