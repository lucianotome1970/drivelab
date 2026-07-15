// ============================================================================
//  DriveLab
//  ConnectionViewModelTests.cs — Testes de ConnectionViewModel (status de conexão/desconexão).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Xunit;
using DriveLab.Studio.Localization;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Tests.ViewModels;

[Collection("Loc")]
public class ConnectionViewModelTests
{
    private static ConnectionViewModel New(out FakeTransport transport)
    {
        transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        return new ConnectionViewModel(session, new ImmediateUiDispatcher());
    }

    [Fact]
    public async Task Connect_Sets_IsConnected_And_Status()
    {
        var vm = New(out _);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.IsConnected);
        Assert.Equal(LocalizationManager.Get("Status_Connected"), vm.StatusText);
    }

    [Fact]
    public async Task Disconnect_Clears_IsConnected()
    {
        var vm = New(out _);
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.DisconnectCommand.ExecuteAsync(null);
        Assert.False(vm.IsConnected);
        Assert.Equal(LocalizationManager.Get("Status_Disconnected"), vm.StatusText);
    }
}
