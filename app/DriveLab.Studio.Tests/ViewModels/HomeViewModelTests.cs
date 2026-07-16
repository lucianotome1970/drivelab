// ============================================================================
//  DriveLab
//  HomeViewModelTests.cs — Testes do dash: clique no card navega só quando o dispositivo está conectado.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class HomeViewModelTests
{
    private sealed class FakePedalStorage : IPedalProfileStorage
    {
        public Task SaveAsync(PedalProfile profile) => Task.CompletedTask;
        public Task<PedalProfile?> LoadAsync() => Task.FromResult<PedalProfile?>(null);
    }

    private sealed class FakeHbStorage : IHandbrakeProfileStorage
    {
        public Task SaveAsync(HandbrakeProfile profile) => Task.CompletedTask;
        public Task<HandbrakeProfile?> LoadAsync() => Task.FromResult<HandbrakeProfile?>(null);
    }

    private static HomeViewModel Make()
    {
        var dispatcher = new ImmediateUiDispatcher();
        var baseSession = new BaseSession(new FakeTransport(), dispatcher); // volante/base desconectados
        var pedals = new PedalsViewModel(new PedalDeviceSession(new FakePedalTransport(), dispatcher), new FakePedalStorage());
        var handbrake = new HandbrakeViewModel(new HandbrakeDeviceSession(new FakeHandbrakeTransport(), dispatcher), new FakeHbStorage());
        return new HomeViewModel(new DashboardViewModel(baseSession), pedals, handbrake, new BaseViewModel(baseSession));
    }

    [Fact]
    public async Task OpenModule_Navigates_Only_When_Device_Connected()
    {
        var vm = Make();
        string? navigatedTo = null;
        vm.ModuleNavigator = key => navigatedTo = key;

        vm.OpenModule("handbrake");                          // desconectado → não navega
        Assert.Null(navigatedTo);

        await vm.Handbrake!.ConnectCommand.ExecuteAsync(null); // dispositivo detectado (Connected → IsConnected)
        vm.OpenModule("handbrake");
        Assert.Equal("handbrake", navigatedTo);
    }

    [Fact]
    public void OpenModule_Ignores_Unknown_Key()
    {
        var vm = Make();
        var called = false;
        vm.ModuleNavigator = _ => called = true;

        vm.OpenModule("nope");
        Assert.False(called);
    }
}
