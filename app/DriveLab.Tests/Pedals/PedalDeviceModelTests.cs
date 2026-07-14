using DriveLab.Core.Pedals;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Tests.Pedals;

public class PedalDeviceModelTests
{
    private static PedalDeviceModel Seeded()
    {
        var m = new PedalDeviceModel();
        m.SeedDefaults();
        return m;
    }

    [Fact]
    public void Defaults_And_Raw_Map_To_Output()
    {
        var m = Seeded();
        m.SetRawInputs(4095, 0, 2048);
        var s = m.BuildState(new FirmwareVersion(0, 1, 0, 0), 0);
        Assert.Equal((ushort)65535, s.Clutch.Output);
        Assert.Equal((ushort)0, s.Brake.Output);
        Assert.InRange(s.Throttle.Output, 32000, 33500);
        Assert.Equal((ushort)4095, s.Clutch.RawInput);
    }

    [Fact]
    public void WriteSetting_Clamps_And_Is_Per_Pedal()
    {
        var m = Seeded();
        m.WriteSetting(PedalSettingId.Smooth, PedalIndex.Brake, new SettingValue(SettingType.UInt8, 250));
        Assert.Equal(100, m.ReadSetting(PedalSettingId.Smooth, PedalIndex.Brake).AsDouble);
        Assert.Equal(0, m.ReadSetting(PedalSettingId.Smooth, PedalIndex.Throttle).AsDouble);
    }

    [Fact]
    public void Calibration_Captures_Observed_Min_Max()
    {
        var m = Seeded();
        m.CalibrateStart(PedalIndex.Brake);
        m.SetRawInputs(0, 300, 0);
        m.SetRawInputs(0, 3800, 0);
        m.SetRawInputs(0, 1500, 0);
        m.CalibrateStop(PedalIndex.Brake);
        Assert.Equal(300, m.ReadSetting(PedalSettingId.InputMin, PedalIndex.Brake).AsDouble);
        Assert.Equal(3800, m.ReadSetting(PedalSettingId.InputMax, PedalIndex.Brake).AsDouble);
    }

    [Fact]
    public void Calibration_With_No_Samples_Keeps_Prior()
    {
        var m = Seeded();
        var priorMin = m.ReadSetting(PedalSettingId.InputMin, PedalIndex.Clutch).AsDouble;
        var priorMax = m.ReadSetting(PedalSettingId.InputMax, PedalIndex.Clutch).AsDouble;
        m.CalibrateStart(PedalIndex.Clutch);
        m.CalibrateStop(PedalIndex.Clutch);
        Assert.Equal(priorMin, m.ReadSetting(PedalSettingId.InputMin, PedalIndex.Clutch).AsDouble);
        Assert.Equal(priorMax, m.ReadSetting(PedalSettingId.InputMax, PedalIndex.Clutch).AsDouble);
    }

    [Fact]
    public void LoadDefaults_Resets()
    {
        var m = Seeded();
        m.WriteSetting(PedalSettingId.Smooth, PedalIndex.Throttle, new SettingValue(SettingType.UInt8, 50));
        m.LoadDefaults();
        Assert.Equal(0, m.ReadSetting(PedalSettingId.Smooth, PedalIndex.Throttle).AsDouble);
    }
}
