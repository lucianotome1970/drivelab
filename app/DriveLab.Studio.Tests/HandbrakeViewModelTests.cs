// ============================================================================
//  DriveLab
//  HandbrakeViewModelTests.cs — Testes de HandbrakeViewModel.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Simulator;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests;

public class HandbrakeViewModelTests
{
    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public void Post(Action action) => action();
    }

    private static HandbrakeViewModel Make(out SimulatorHandbrakeTransport t)
    {
        t = new SimulatorHandbrakeTransport();
        var session = new HandbrakeDeviceSession(t, new ImmediateDispatcher());
        return new HandbrakeViewModel(session, new JsonHandbrakeProfileStorage(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hbvm-{System.Guid.NewGuid():N}.json")));
    }

    [Fact]
    public async Task State_Updates_Output_And_Button()
    {
        var vm = Make(out var t);
        await vm.ConnectCommand.ExecuteAsync(null);
        t.StopStreaming();
        t.SetRawInput(4095);
        t.Step();
        Assert.Equal(1.0, vm.CurrentOutput01, 2);
        Assert.True(vm.ButtonActive);
        vm.Dispose();
    }

    [Fact]
    public async Task Connecting_Exposes_CurveEditor_Contract()
    {
        var vm = Make(out _);
        Assert.Equal(6, vm.Points.Count);
        Assert.Equal("0%", vm.Points[0].Label);
        Assert.Equal("100%", vm.Points[5].Label);
        Assert.False(vm.CanEdit);

        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.CanEdit);
        vm.Dispose();
    }
}
