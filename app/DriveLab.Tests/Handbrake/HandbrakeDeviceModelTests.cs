// ============================================================================
//  DriveLab
//  HandbrakeDeviceModelTests.cs — Testes de HandbrakeDeviceModel: mapeamento, botão e calibração.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Handbrake;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Tests.Handbrake;

public class HandbrakeDeviceModelTests
{
    private static HandbrakeDeviceModel Seeded()
    {
        var m = new HandbrakeDeviceModel();
        m.SeedDefaults();
        return m;
    }

    [Fact]
    public void Raw_Maps_To_Axis_In_Clutch_Slot()
    {
        var m = Seeded();
        m.SetRawInput(4095);
        var s = m.BuildState(new FirmwareVersion(0, 1, 0, 0));
        Assert.Equal((ushort)65535, s.Clutch.Output);
        Assert.Equal((ushort)4095, s.Clutch.RawInput);
    }

    [Fact]
    public void Button_Presses_Above_Threshold_When_Enabled()
    {
        var m = Seeded(); // ButtonThreshold default 70%, enabled
        m.SetRawInput(4095); // 100%
        Assert.True(m.ButtonPressed);
        var s = m.BuildState(new FirmwareVersion(0, 1, 0, 0));
        Assert.Equal(HandbrakeFlags.ButtonPressed, (HandbrakeFlags)(s.Flags & (byte)HandbrakeFlags.ButtonPressed));
    }

    [Fact]
    public void Button_Released_Below_Threshold()
    {
        var m = Seeded();
        m.SetRawInput(0);
        Assert.False(m.ButtonPressed);
    }

    [Fact]
    public void Button_Never_Presses_When_Disabled()
    {
        var m = Seeded();
        m.WriteSetting(HandbrakeSettingId.ButtonEnabled, new SettingValue(SettingType.UInt8, 0));
        m.SetRawInput(4095);
        Assert.False(m.ButtonPressed);
    }

    [Fact]
    public void Hysteresis_Keeps_Button_Pressed_Just_Below_Threshold()
    {
        var m = Seeded();
        m.WriteSetting(HandbrakeSettingId.ButtonThreshold, new SettingValue(SettingType.UInt8, 50));
        m.SetRawInput(4095);            // 100% -> pressed
        Assert.True(m.ButtonPressed);
        m.SetRawInput((ushort)(4095 * 0.49)); // 49% -> within 3% band below 50% -> stays pressed
        Assert.True(m.ButtonPressed);
        m.SetRawInput((ushort)(4095 * 0.40)); // 40% -> below band -> releases
        Assert.False(m.ButtonPressed);
    }

    [Fact]
    public void WriteSetting_Clamps()
    {
        var m = Seeded();
        m.WriteSetting(HandbrakeSettingId.Smooth, new SettingValue(SettingType.UInt8, 250));
        Assert.Equal(100, m.ReadSetting(HandbrakeSettingId.Smooth).AsDouble);
    }

    [Fact]
    public void Calibration_Captures_Observed_Min_Max()
    {
        var m = Seeded();
        m.CalibrateStart();
        m.SetRawInput(500);
        m.SetRawInput(3000);
        m.SetRawInput(1500);
        m.CalibrateStop();
        Assert.Equal(500, m.ReadSetting(HandbrakeSettingId.InputMin).AsDouble);
        Assert.Equal(3000, m.ReadSetting(HandbrakeSettingId.InputMax).AsDouble);
    }
}
