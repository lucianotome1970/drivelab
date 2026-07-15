// ============================================================================
//  DriveLab
//  HardwareMonitorViewModelTests.cs — Testes de HardwareMonitorViewModel (formatação e níveis de tensão/corrente/temperatura).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class HardwareMonitorViewModelTests
{
    private static (HardwareMonitorViewModel vm, FakeTransport t) Make()
    {
        var t = new FakeTransport();
        var session = new BaseSession(t, new ImmediateUiDispatcher());
        return (new HardwareMonitorViewModel(session), t);
    }

    [Fact]
    public void Formats_Voltage_Current_And_Temps()
    {
        var (vm, t) = Make();
        t.Emit(new BaseState { BusVoltageMv = 24000, MotorCurrentMa = 1500, FetTempC = 42, MotorTempC = 55, McuTempC = 45 });
        Assert.Equal("24.0 V", vm.BusVoltageText);
        Assert.Equal("1.50 A", vm.MotorCurrentText);
        Assert.Equal("42 °C", vm.FetTempText);
    }

    [Fact]
    public void Sentinel_Temperature_Shows_No_Sensor()
    {
        var (vm, t) = Make();
        t.Emit(new BaseState { MotorTempC = -128 });
        Assert.Equal("—", vm.MotorTempText);
    }

    [Fact]
    public void Classifies_Levels_By_Threshold()
    {
        var (vm, t) = Make();
        t.Emit(new BaseState { BusVoltageMv = 24000, FetTempC = 85 });
        Assert.Equal(TelemetryLevel.Ok, vm.BusVoltageLevel);
        Assert.Equal(TelemetryLevel.Critical, vm.FetTempLevel);
    }

    [Theory]
    [InlineData(15999, TelemetryLevel.Critical)]  // < 16000
    [InlineData(16000, TelemetryLevel.Warning)]   // in [16000,18000)
    [InlineData(17999, TelemetryLevel.Warning)]
    [InlineData(18000, TelemetryLevel.Ok)]        // Ok band start
    [InlineData(24000, TelemetryLevel.Ok)]
    [InlineData(26000, TelemetryLevel.Ok)]        // Ok band end (inclusive)
    [InlineData(26001, TelemetryLevel.Warning)]
    [InlineData(28000, TelemetryLevel.Warning)]
    [InlineData(28001, TelemetryLevel.Critical)]  // > 28000
    [InlineData(65535, TelemetryLevel.Critical)]  // u16 max survives (signedness guard)
    public void Voltage_Level_Boundaries(int mv, TelemetryLevel expected)
    {
        var (vm, t) = Make();
        t.Emit(new BaseState { BusVoltageMv = (ushort)mv });
        Assert.Equal(expected, vm.BusVoltageLevel);
    }

    [Theory]
    [InlineData((sbyte)59, TelemetryLevel.Ok)]        // < 60
    [InlineData((sbyte)60, TelemetryLevel.Warning)]   // warn band start
    [InlineData((sbyte)79, TelemetryLevel.Warning)]
    [InlineData((sbyte)80, TelemetryLevel.Critical)]  // >= 80
    [InlineData((sbyte)-128, TelemetryLevel.Ok)]      // no-sensor sentinel classified Ok
    public void Temperature_Level_Boundaries(sbyte c, TelemetryLevel expected)
    {
        var (vm, t) = Make();
        t.Emit(new BaseState { FetTempC = c });
        Assert.Equal(expected, vm.FetTempLevel);
    }
}
