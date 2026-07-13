using Xunit;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Tests.ViewModels;

public class DashboardViewModelTests
{
    private static DashboardViewModel New(out FakeTransport transport)
    {
        transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        return new DashboardViewModel(session);
    }

    [Fact]
    public void Telemetry_Updates_AngleDegrees_And_PositionPercent()
    {
        var vm = New(out var transport);
        transport.Emit(new DeviceState { AngleDeciDeg = 2700, Position = 5000 });

        Assert.Equal(270.0, vm.AngleDegrees, precision: 3);   // 2700 deci-deg = 270°
        Assert.Equal(50.0, vm.PositionPercent, precision: 3);  // 5000/10000 = 50%
    }

    [Fact]
    public async Task CenterCommand_Sends_ResetCenter()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        await vm.CenterCommand.ExecuteAsync(null);
        Assert.Equal(DeviceCommand.ResetCenter, transport.LastCommand!.Value.cmd);
    }

    [Fact]
    public async Task SetMaxAngle_Writes_MotionRange_Setting()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        await vm.SetMaxAngleCommand.ExecuteAsync("900");

        Assert.Equal(SettingId.MotionRange, transport.LastWrite!.Value.id);
        Assert.Equal(900, transport.LastWrite!.Value.value.AsDouble);
        Assert.Equal(900, vm.MotionRange);
    }

    [Fact]
    public async Task Center_Does_Nothing_When_Disconnected()
    {
        var vm = New(out var transport); // not connected
        await vm.CenterCommand.ExecuteAsync(null);
        Assert.Null(transport.LastCommand);
    }
}
