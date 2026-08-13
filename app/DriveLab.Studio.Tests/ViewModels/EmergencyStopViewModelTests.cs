// ============================================================================
//  DriveLab
//  EmergencyStopViewModelTests.cs — Testes do E-stop: parar corta a força (SetForceEnabled=0); rearmar liga.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Threading.Tasks;
using Xunit;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Tests.ViewModels;

public class EmergencyStopViewModelTests
{
    private sealed class NoopDispatcher : IUiDispatcher { public void Post(System.Action a) => a(); }

    private static (EmergencyStopViewModel vm, FakeTransport transport) Build()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new NoopDispatcher());
        return (new EmergencyStopViewModel(session), transport);
    }

    [Fact]
    public async Task Stop_CutsForce_AndEngages()
    {
        var (vm, transport) = Build();
        Assert.False(vm.Engaged);

        await vm.StopCommand.ExecuteAsync(null);

        Assert.True(vm.Engaged);
        Assert.Equal((BaseCommand.SetForceEnabled, (byte)0), transport.LastCommand);
    }

    [Fact]
    public async Task Rearm_EnablesForce_AndDisengages()
    {
        var (vm, transport) = Build();
        await vm.StopCommand.ExecuteAsync(null);

        await vm.RearmCommand.ExecuteAsync(null);

        Assert.False(vm.Engaged);
        Assert.Equal((BaseCommand.SetForceEnabled, (byte)1), transport.LastCommand);
    }
}
