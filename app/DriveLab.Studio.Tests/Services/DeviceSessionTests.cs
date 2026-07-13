using Xunit;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.Tests.Services;

public class DeviceSessionTests
{
    private static DeviceSession NewSession(out FakeTransport transport)
    {
        transport = new FakeTransport();
        return new DeviceSession(transport, new ImmediateUiDispatcher());
    }

    [Fact]
    public async Task ConnectAsync_Connects_Underlying_Transport()
    {
        var session = NewSession(out var transport);
        await session.ConnectAsync();
        Assert.True(session.IsConnected);
        Assert.Equal(1, transport.ConnectCalls);
    }

    [Fact]
    public async Task DisconnectAsync_Raises_Disconnected()
    {
        var session = NewSession(out _);
        await session.ConnectAsync();
        var raised = false;
        session.Disconnected += (_, _) => raised = true;

        await session.DisconnectAsync();

        Assert.True(raised);
    }

    [Fact]
    public async Task WriteSettingAsync_Raises_SettingChanged_With_Id_And_Value()
    {
        var session = NewSession(out _);
        SettingChangedEventArgs? got = null;
        session.SettingChanged += (_, e) => got = e;

        await session.WriteSettingAsync(SettingId.MotionRange, new SettingValue(SettingType.UInt16, 360));

        Assert.NotNull(got);
        Assert.Equal(SettingId.MotionRange, got!.Id);
        Assert.Equal(360, got.Value.AsDouble);
    }

    [Fact]
    public void StateReceived_Is_Re_Raised_Through_Dispatcher()
    {
        var session = NewSession(out var transport);
        DeviceState? got = null;
        session.StateReceived += (_, s) => got = s;

        transport.Emit(new DeviceState { AngleDeciDeg = 1234 });

        Assert.NotNull(got);
        Assert.Equal(1234, got!.AngleDeciDeg);
    }

    [Fact]
    public async Task SendCommandAsync_Passes_Through()
    {
        var session = NewSession(out var transport);
        await session.SendCommandAsync(DeviceCommand.ResetCenter);
        Assert.Equal(DeviceCommand.ResetCenter, transport.LastCommand!.Value.cmd);
    }

    [Fact]
    public async Task DisconnectAsync_Disconnects()
    {
        var session = NewSession(out var transport);
        await session.ConnectAsync();
        await session.DisconnectAsync();
        Assert.False(session.IsConnected);
        Assert.Equal(1, transport.DisconnectCalls);
    }
}
