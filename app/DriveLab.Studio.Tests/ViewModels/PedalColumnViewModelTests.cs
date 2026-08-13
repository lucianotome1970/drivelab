// ============================================================================
//  DriveLab
//  PedalColumnViewModelTests.cs — Testes de PedalColumnViewModel (curva, calibração, presets e telemetria por pedal).
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

public class PedalColumnViewModelTests
{
    private static (PedalColumnViewModel vm, FakePedalTransport t, PedalDeviceSession s) Make(bool connected)
    {
        var t = new FakePedalTransport();
        var s = new PedalDeviceSession(t, new ImmediateUiDispatcher());
        if (connected)
        {
#pragma warning disable xUnit1031
            s.ConnectAsync().GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        }
        var vm = new PedalColumnViewModel(s, PedalIndex.Brake, "Freio");
        return (vm, t, s);
    }

    [Fact]
    public void Has_Six_Curve_Points()
    {
        var (vm, _, s) = Make(false);
        Assert.Equal(6, vm.Points.Count);
        Assert.Equal("0%", vm.Points[0].Label);
        Assert.Equal("100%", vm.Points[5].Label);
        s.Dispose();
    }

    [Fact]
    public void Editing_Point_Writes_When_Connected()
    {
        var (vm, t, s) = Make(connected: true);
        vm.Points[2].Value = 55;
        Assert.NotNull(t.LastWrite);
        Assert.Equal(PedalSettingId.CurvePoint2, t.LastWrite!.Value.id);
        Assert.Equal(PedalIndex.Brake, t.LastWrite.Value.pedal);
        Assert.Equal(55, t.LastWrite.Value.value.AsDouble);
        s.Dispose();
    }

    [Fact]
    public void Editing_Point_Does_Not_Write_When_Disconnected()
    {
        var (vm, t, s) = Make(connected: false);
        vm.Points[2].Value = 55;
        Assert.Null(t.LastWrite);
        s.Dispose();
    }

    [Fact]
    public void CanEdit_Follows_Connection()
    {
        var (vm, _, s) = Make(connected: false);
        Assert.False(vm.CanEdit);
#pragma warning disable xUnit1031
        s.ConnectAsync().GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        Assert.True(vm.CanEdit);
        s.Dispose();
    }

    [Fact]
    public void ApplyPreset_Sets_All_Points()
    {
        var (vm, _, s) = Make(connected: true);
        vm.ApplyPreset(new PedalCurvePreset("Zero", new double[] { 0, 0, 0, 0, 0, 0 }));
        Assert.All(vm.Points, p => Assert.Equal(0, p.Value));
        s.Dispose();
    }

    [Fact]
    public void Telemetry_Updates_Live_Values()
    {
        var (vm, t, s) = Make(connected: true);
        t.Emit(new PedalState { Brake = new PedalReading(2048, 32768) });
        Assert.InRange(vm.CurrentOutput01, 0.49, 0.51);
        Assert.Single(vm.LiveValues);
        s.Dispose();
    }

    [Fact]
    public void SelectPreset_Applies_To_This_Column_And_Marks_Selected()
    {
        var (vm, _, s) = Make(connected: true);
        var preset = new PedalCurvePreset("Zero", new double[] { 0, 0, 0, 0, 0, 0 });
        vm.SelectPresetCommand.Execute(preset);
        Assert.All(vm.Points, p => Assert.Equal(0, p.Value));
        s.Dispose();
    }

    [Fact]
    public void SelectPreset_Marks_Only_That_Option_Selected()
    {
        var (vm, _, s) = Make(connected: true);
        var linear = vm.PresetOptions.First(o => o.Name == "Linear");
        vm.SelectPresetCommand.Execute(linear.Preset);
        Assert.True(linear.IsSelected);
        Assert.All(vm.PresetOptions.Where(o => o.Name != "Linear"), o => Assert.False(o.IsSelected));
        s.Dispose();
    }

    [Fact]
    public void SelectSensor_Sets_SensorType()
    {
        var (vm, _, s) = Make(connected: true);
        vm.SelectSensorCommand.Execute("2");
        Assert.Equal(2, vm.SensorType);
        s.Dispose();
    }

    [Fact]
    public void Column_Exposes_Preset_Options()
    {
        var (vm, _, s) = Make(connected: false);
        Assert.Contains(vm.PresetOptions, o => o.Name == "Linear");
        s.Dispose();
    }

    [Fact]
    public void ConfigurableSource_Shows_Sensor_And_Not_Preview()
    {
        var t = new FakePedalTransport { SupportsConfig = true };
        var s = new PedalDeviceSession(t, new ImmediateUiDispatcher(), "Simulador");
        var vm = new PedalColumnViewModel(s, PedalIndex.Brake, "Freio");
        Assert.True(vm.ShowSensor);
        Assert.False(vm.CurvePreviewOnly);
        s.Dispose();
    }

    [Fact]
    public void ReadOnlySource_Hides_Sensor_And_Marks_Preview()
    {
        var t = new FakePedalTransport { SupportsConfig = false };
        var s = new PedalDeviceSession(t, new ImmediateUiDispatcher(), "Simagic P2000 — leitura");
        var vm = new PedalColumnViewModel(s, PedalIndex.Brake, "Freio");
        Assert.False(vm.ShowSensor);
        Assert.True(vm.CurvePreviewOnly);
        s.Dispose();
    }

    [Fact]
    public void Deadzone_Edit_Writes_When_Connected()
    {
        var (vm, t, s) = Make(connected: true);
        vm.DeadzoneLow = 8;
        Assert.Equal(PedalSettingId.DeadzoneLow, t.LastWrite!.Value.id);
        Assert.Equal(8, t.LastWrite.Value.value.AsDouble);
        s.Dispose();
    }

    [Fact]
    public void Brake_Force_Kg_From_Input_And_Max()
    {
        var t = new FakePedalTransport();
        var s = new PedalDeviceSession(t, new ImmediateUiDispatcher());
        var vm = new PedalColumnViewModel(s, PedalIndex.Brake, "Freio") { LoadCellMaxKg = 100 };
        t.Emit(new PedalState { Brake = new PedalReading(2048, 30000) }); // input ~50%
        Assert.True(vm.IsBrake);
        Assert.InRange(vm.CurrentForceKg, 49, 51);
        s.Dispose();
    }

    [Fact]
    public void Capture_Tracks_Cal_Min_Max()
    {
        var t = new FakePedalTransport();
        var s = new PedalDeviceSession(t, new ImmediateUiDispatcher());
        var vm = new PedalColumnViewModel(s, PedalIndex.Brake, "Freio");
        vm.BeginCapture();
        t.Emit(new PedalState { Brake = new PedalReading(300, 0) });
        t.Emit(new PedalState { Brake = new PedalReading(3800, 0) });
        t.Emit(new PedalState { Brake = new PedalReading(1500, 0) });
        Assert.Equal(300, vm.CalMin);
        Assert.Equal(3800, vm.CalMax);
        vm.EndCapture();
        Assert.False(vm.Capturing);
        s.Dispose();
    }

    [Fact]
    public void Throttle_Column_Is_Not_Brake()
    {
        var t = new FakePedalTransport();
        var s = new PedalDeviceSession(t, new ImmediateUiDispatcher());
        var vm = new PedalColumnViewModel(s, PedalIndex.Throttle, "Acelerador");
        Assert.False(vm.IsBrake);
        s.Dispose();
    }

    [Fact]
    public void Presets_Include_Linear()
    {
        Assert.Contains(PedalCurvePresets.All, p => p.Name == "Linear");
    }

    [Fact]
    public void Calibrate_Toggles_And_Sends_Start_Then_Stop()
    {
        var (vm, t, s) = Make(connected: true);
        vm.CalibrateCommand.Execute(null);
        Assert.True(vm.IsCalibrating);
        Assert.Equal(PedalCommandId.CalibrateStart, t.LastCommand!.Value.cmd);
        Assert.Equal((byte)PedalIndex.Brake, t.LastCommand.Value.arg);

        vm.CalibrateCommand.Execute(null);
        Assert.False(vm.IsCalibrating);
        Assert.Equal(PedalCommandId.CalibrateStop, t.LastCommand.Value.cmd);
        s.Dispose();
    }

    [Fact]
    public void InputMax_Edit_Writes_When_Connected()
    {
        var (vm, t, s) = Make(connected: true);
        vm.InputMax = 3000;
        Assert.Equal(PedalSettingId.InputMax, t.LastWrite!.Value.id);
        Assert.Equal(3000, t.LastWrite.Value.value.AsDouble);
        s.Dispose();
    }
}
