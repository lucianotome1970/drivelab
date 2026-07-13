using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class SettingFieldViewModelTests
{
    private static SettingFieldViewModel New(out FakeTransport transport)
    {
        transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        return new SettingFieldViewModel(session, SettingsSchema.Get(SettingId.MotionRange));
    }

    [Fact]
    public void Exposes_Descriptor_Metadata_And_Default()
    {
        var vm = New(out _);
        Assert.Equal("Ângulo total de giro", vm.DisplayName);
        Assert.Equal(90, vm.Min);
        Assert.Equal(2000, vm.Max);
        Assert.Equal(900, vm.Value);
    }

    [Fact]
    public async Task WriteAsync_Sends_Clamped_Value_To_Device()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        vm.Value = 750;
        await vm.WriteAsync();
        Assert.Equal(SettingId.MotionRange, transport.LastWrite!.Value.id);
        Assert.Equal(750, transport.LastWrite!.Value.value.AsDouble);
    }

    [Fact]
    public async Task LoadAsync_Reads_Value_Without_Writing_Back()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        await vm.LoadAsync();
        Assert.Equal(900, vm.Value);         // FakeTransport.ReadSettingAsync returns 900
        Assert.Null(transport.LastWrite);    // load must not trigger a write
    }

    [Fact]
    public async Task WriteAsync_Does_Nothing_When_Disconnected()
    {
        var vm = New(out var transport); // transport NOT connected
        vm.Value = 750;
        await vm.WriteAsync();
        Assert.Null(transport.LastWrite);
    }
}
