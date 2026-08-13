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
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
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
    public void Series_Has_Three_Line_Series()
    {
        // posição, torque e clipping — as três no MESMO gráfico, porque clipping só significa algo
        // no contexto das outras duas.
        var vm = New(out _);
        Assert.Equal(3, vm.Series.Length);
    }

    [Fact]
    public void State_Appends_Clipping_As_Percent()
    {
        // Clipping vai ao MESMO gráfico das outras duas de propósito: ele só significa algo no
        // contexto (clipping alto COM torque no teto = força saturando; clipping alto com torque
        // baixo = outra história). Chega como byte 0..255 e é plotado em 0..100%.
        var vm = New(out var transport);
        transport.Emit(new BaseState { Position = 0, Torque = 0, Clipping = 255 });

        Assert.Single(vm.ClippingSamples);
        Assert.Equal(100.0, vm.ClippingSamples[0].Value);
    }

    [Fact]
    public void Clipping_Series_Respects_The_Same_Window()
    {
        var vm = New(out var transport);
        for (var i = 0; i < 300; i++)
            transport.Emit(new BaseState { Clipping = 128 });

        Assert.Equal(240, vm.ClippingSamples.Count);   // mesma janela das outras séries
    }
}
