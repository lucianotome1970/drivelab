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
        var value = await transport.ReadSettingAsync(SettingId.MotionRange);
        Assert.Equal(900, value.AsDouble);
    }

    [Fact]
    public async Task WriteSetting_Clamps_And_Persists()
    {
        var transport = await ConnectedAsync();

        await transport.WriteSettingAsync(SettingId.TotalStrength, new SettingValue(SettingType.UInt8, 250));
        var value = await transport.ReadSettingAsync(SettingId.TotalStrength);

        Assert.Equal(100, value.AsDouble); // clamped to max
    }

    [Fact]
    public async Task Streaming_Step_Raises_StateReceived()
    {
        var transport = await ConnectedAsync();
        DeviceState? received = null;
        transport.StateReceived += (_, state) => received = state;

        transport.Step(0.01);

        Assert.NotNull(received);
        Assert.True(received!.Flags.HasFlag(DeviceFlags.UsingSimulator));
    }

    [Fact]
    public async Task DirectControl_Then_Step_Moves_Reported_Position()
    {
        var transport = await ConnectedAsync();
        await transport.SendCommandAsync(DeviceCommand.SetForceEnabled, 1);
        await transport.SendDirectControlAsync(new DirectControl { ConstantForce = 5000 });

        DeviceState? last = null;
        transport.StateReceived += (_, state) => last = state;
        for (var i = 0; i < 50; i++) transport.Step(0.01);

        Assert.NotNull(last);
        Assert.True(last!.Position > 0, "position should move under positive constant force");
    }

    [Fact]
    public async Task ResetCenter_Command_Recenters()
    {
        var transport = await ConnectedAsync();
        await transport.SendDirectControlAsync(new DirectControl { ConstantForce = 5000 });
        for (var i = 0; i < 50; i++) transport.Step(0.01);

        await transport.SendCommandAsync(DeviceCommand.ResetCenter);

        DeviceState? last = null;
        transport.StateReceived += (_, state) => last = state;
        transport.Step(0.0);
        Assert.Equal(0, last!.Position);
    }
}
