// ============================================================================
//  DriveLab
//  TelemetryViewModelTests.cs — Testes de TelemetryViewModel (amostras de posição e torque).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class TelemetryViewModelTests
{
    private static TelemetryViewModel New(out FakeTransport transport)
    {
        transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        return new TelemetryViewModel(session);
    }

    [Fact]
    public void State_Appends_Normalized_Position_And_Torque()
    {
        var vm = New(out var transport);
        transport.Emit(new BaseState { Position = 5000, Torque = 9000 });

        Assert.Single(vm.PositionSamples);
        Assert.Single(vm.TorqueSamples);
        Assert.Equal(50.0, vm.PositionSamples[0].Value);
        Assert.Equal(90.0, vm.TorqueSamples[0].Value);
    }

    [Fact]
    public void Samples_Are_Capped_At_240()
    {
        var vm = New(out var transport);
        for (var i = 0; i < 300; i++)
            transport.Emit(new BaseState { Position = 100, Torque = 100 });

        Assert.Equal(240, vm.PositionSamples.Count);
        Assert.Equal(240, vm.TorqueSamples.Count);
    }

    [Fact]
    public void Dispose_Stops_Appending()
    {
        var vm = New(out var transport);
        transport.Emit(new BaseState { Position = 100 });
        Assert.Single(vm.PositionSamples);

        vm.Dispose();
        transport.Emit(new BaseState { Position = 200 });
        Assert.Single(vm.PositionSamples); // unchanged after dispose
    }

    [Fact]
    public void Series_Has_Two_Line_Series()
    {
        var vm = New(out _);
        Assert.Equal(2, vm.Series.Length);
    }
}
