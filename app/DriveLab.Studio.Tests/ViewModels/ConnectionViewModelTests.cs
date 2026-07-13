using Xunit;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Tests.ViewModels;

public class ConnectionViewModelTests
{
    private static ConnectionViewModel New(out FakeTransport transport)
    {
        transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        return new ConnectionViewModel(session);
    }

    [Fact]
    public async Task Connect_Sets_IsConnected_And_Status()
    {
        var vm = New(out _);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.IsConnected);
        Assert.Equal("Conectado", vm.StatusText);
    }

    [Fact]
    public async Task Disconnect_Clears_IsConnected()
    {
        var vm = New(out _);
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.DisconnectCommand.ExecuteAsync(null);
        Assert.False(vm.IsConnected);
        Assert.Equal("Desconectado", vm.StatusText);
    }
}
