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
        var session = new DeviceSession(t, new ImmediateUiDispatcher());
        return (new HardwareMonitorViewModel(session), t);
    }

    [Fact]
    public void Formats_Voltage_Current_And_Temps()
    {
        var (vm, t) = Make();
        t.Emit(new DeviceState { BusVoltageMv = 24000, MotorCurrentMa = 1500, FetTempC = 42, MotorTempC = 55, McuTempC = 45 });
        Assert.Equal("24.0 V", vm.BusVoltageText);
        Assert.Equal("1.50 A", vm.MotorCurrentText);
        Assert.Equal("42 °C", vm.FetTempText);
    }

    [Fact]
    public void Sentinel_Temperature_Shows_No_Sensor()
    {
        var (vm, t) = Make();
        t.Emit(new DeviceState { MotorTempC = -128 });
        Assert.Equal("—", vm.MotorTempText);
    }

    [Fact]
    public void Classifies_Levels_By_Threshold()
    {
        var (vm, t) = Make();
        t.Emit(new DeviceState { BusVoltageMv = 24000, FetTempC = 85 });
        Assert.Equal(TelemetryLevel.Ok, vm.BusVoltageLevel);
        Assert.Equal(TelemetryLevel.Critical, vm.FetTempLevel);
    }
}
