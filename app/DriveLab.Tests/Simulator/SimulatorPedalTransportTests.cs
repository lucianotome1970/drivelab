using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Simulator;

namespace DriveLab.Tests.Simulator;

public class SimulatorPedalTransportTests
{
    [Fact]
    public async Task Connect_Seeds_Defaults()
    {
        var t = new SimulatorPedalTransport();
        await t.ConnectAsync();
        Assert.True(t.IsConnected);
        var v = await t.ReadSettingAsync(PedalSettingId.InputMax, PedalIndex.Brake);
        Assert.Equal(4095, v.AsDouble);
    }

    [Fact]
    public async Task WriteSetting_Clamps_And_Persists()
    {
        var t = new SimulatorPedalTransport();
        await t.ConnectAsync();
        await t.WriteSettingAsync(PedalSettingId.Smooth, PedalIndex.Throttle, new SettingValue(SettingType.UInt8, 250));
        var v = await t.ReadSettingAsync(PedalSettingId.Smooth, PedalIndex.Throttle);
        Assert.Equal(100, v.AsDouble); // clamp 0..100
    }

    [Fact]
    public async Task Settings_Are_Per_Pedal()
    {
        var t = new SimulatorPedalTransport();
        await t.ConnectAsync();
        await t.WriteSettingAsync(PedalSettingId.Invert, PedalIndex.Clutch, new SettingValue(SettingType.UInt8, 1));
        var clutch = await t.ReadSettingAsync(PedalSettingId.Invert, PedalIndex.Clutch);
        var brake = await t.ReadSettingAsync(PedalSettingId.Invert, PedalIndex.Brake);
        Assert.Equal(1, clutch.AsDouble);
        Assert.Equal(0, brake.AsDouble);
    }

    [Fact]
    public async Task SetRawInputs_Then_Step_Emits_State_With_Output()
    {
        var t = new SimulatorPedalTransport();
        await t.ConnectAsync();
        PedalState? received = null;
        t.StateReceived += (_, s) => received = s;

        t.SetRawInputs(4095, 0, 2048);
        t.Step();

        Assert.NotNull(received);
        Assert.Equal((ushort)65535, received!.Clutch.Output);
        Assert.Equal((ushort)0, received.Brake.Output);
        Assert.InRange(received.Throttle.Output, 32000, 33500);
    }

    [Fact]
    public async Task LoadDefaults_Resets_Settings()
    {
        var t = new SimulatorPedalTransport();
        await t.ConnectAsync();
        await t.WriteSettingAsync(PedalSettingId.Smooth, PedalIndex.Brake, new SettingValue(SettingType.UInt8, 50));
        await t.SendCommandAsync(PedalCommandId.LoadDefaults);
        var v = await t.ReadSettingAsync(PedalSettingId.Smooth, PedalIndex.Brake);
        Assert.Equal(0, v.AsDouble);
    }

    [Fact]
    public async Task Calibration_Captures_Observed_Min_Max()
    {
        var t = new SimulatorPedalTransport();
        await t.ConnectAsync();

        await t.SendCommandAsync(PedalCommandId.CalibrateStart, (byte)PedalIndex.Brake);
        t.SetRawInputs(0, 300, 0);
        t.SetRawInputs(0, 3800, 0);
        t.SetRawInputs(0, 1500, 0);
        await t.SendCommandAsync(PedalCommandId.CalibrateStop, (byte)PedalIndex.Brake);

        var min = await t.ReadSettingAsync(PedalSettingId.InputMin, PedalIndex.Brake);
        var max = await t.ReadSettingAsync(PedalSettingId.InputMax, PedalIndex.Brake);
        Assert.Equal(300, min.AsDouble);
        Assert.Equal(3800, max.AsDouble);
    }
}
