// ============================================================================
//  DriveLab
//  SimulatorTransportTests.cs — Testes do SimulatorTransport.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Simulator;

namespace DriveLab.Tests.Simulator;

public class SimulatorTransportTests
{
    private static async Task<SimulatorTransport> ConnectedAsync()
    {
        var transport = new SimulatorTransport();
        await transport.ConnectAsync();
        return transport;
    }

    [Fact]
    public async Task Connect_Sets_IsConnected_And_Seeds_Defaults()
    {
        var transport = await ConnectedAsync();

        Assert.True(transport.IsConnected);
        var value = await transport.ReadSettingAsync(BaseSettingId.MotionRange);
        Assert.Equal(900, value.AsDouble);
    }

    [Fact]
    public async Task WriteSetting_Clamps_And_Persists()
    {
        var transport = await ConnectedAsync();

        await transport.WriteSettingAsync(BaseSettingId.TotalStrength, new SettingValue(SettingType.UInt8, 250));
        var value = await transport.ReadSettingAsync(BaseSettingId.TotalStrength);

        Assert.Equal(100, value.AsDouble); // clamped to max
    }

    [Fact]
    public async Task Streaming_Step_Raises_StateReceived()
    {
        var transport = await ConnectedAsync();
        BaseState? received = null;
        transport.StateReceived += (_, state) => received = state;

        transport.Step(0.01);

        Assert.NotNull(received);
        Assert.True(received!.Flags.HasFlag(BaseFlags.UsingSimulator));
    }

    [Fact]
    public async Task DirectControl_Then_Step_Moves_Reported_Position()
    {
        var transport = await ConnectedAsync();
        await transport.SendCommandAsync(BaseCommand.SetForceEnabled, 1);
        await transport.SendDirectControlAsync(new BaseDirectControl { ConstantForce = 5000 });

        BaseState? last = null;
        transport.StateReceived += (_, state) => last = state;
        for (var i = 0; i < 50; i++) transport.Step(0.01);

        Assert.NotNull(last);
        Assert.True(last!.Position > 0, "position should move under positive constant force");
    }

    [Fact]
    public async Task BuildState_Includes_Synthetic_Telemetry()
    {
        var transport = await ConnectedAsync();
        BaseState? received = null;
        transport.StateReceived += (_, state) => received = state;

        transport.Step(0.01);

        Assert.NotNull(received);
        Assert.Equal((ushort)24000, received!.BusVoltageMv);
        Assert.Equal(38, received.FetTempC);
        Assert.Equal(42, received.MotorTempC);
        Assert.Equal(45, received.McuTempC);
    }

    [Fact]
    public async Task ResetCenter_Command_Recenters()
    {
        var transport = await ConnectedAsync();
        await transport.SendDirectControlAsync(new BaseDirectControl { ConstantForce = 5000 });
        for (var i = 0; i < 50; i++) transport.Step(0.01);

        await transport.SendCommandAsync(BaseCommand.ResetCenter);

        BaseState? last = null;
        transport.StateReceived += (_, state) => last = state;
        transport.Step(0.0);
        Assert.Equal(0, last!.Position);
    }
}
