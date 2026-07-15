// ============================================================================
//  DriveLab
//  TestViewModelTests.cs — Testes de TestViewModel (envio de força direta e habilitação de força).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Transport;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class TestViewModelTests
{
    private static TestViewModel New(out FakeTransport transport)
    {
        transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        return new TestViewModel(session);
    }

    [Fact]
    public async Task SendAsync_Sends_Scaled_DirectControl_When_Connected()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        vm.Constant = 0.5;
        vm.Spring = -0.3;

        await vm.SendAsync();

        Assert.Equal(5000, transport.LastControl!.ConstantForce);
        Assert.Equal(-3000, transport.LastControl!.SpringForce);
    }

    [Fact]
    public async Task SendAsync_Does_Nothing_When_Disconnected()
    {
        var vm = New(out var transport); // not connected
        vm.Constant = 0.5;
        await vm.SendAsync();
        Assert.Null(transport.LastControl);
    }

    [Fact]
    public async Task ForceEnabled_Sends_SetForceEnabled_Command()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        vm.ForceEnabled = true;
        Assert.Equal(DeviceCommand.SetForceEnabled, transport.LastCommand!.Value.cmd);
        Assert.Equal(1, transport.LastCommand!.Value.arg);
    }

    [Fact]
    public void ForceEnabled_Does_Nothing_When_Disconnected()
    {
        var vm = New(out var transport); // not connected
        vm.ForceEnabled = true;
        Assert.Null(transport.LastCommand);
    }
}
