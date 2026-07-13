using Xunit;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.Tests.Services;

public class PedalDeviceSessionTests
{
    private static PedalDeviceSession Make(FakePedalTransport t) =>
        new(t, new ImmediateUiDispatcher());

    [Fact]
    public async Task Connect_Sets_State_And_Raises_Event()
    {
        var t = new FakePedalTransport();
        using var s = Make(t);
        var raised = false;
        s.Connected += (_, _) => raised = true;

        await s.ConnectAsync();

        Assert.True(s.IsConnected);
        Assert.True(raised);
    }

    [Fact]
    public async Task WriteSetting_Forwards_And_Raises_SettingChanged()
    {
        var t = new FakePedalTransport();
        using var s = Make(t);
        PedalSettingChangedEventArgs? args = null;
        s.SettingChanged += (_, e) => args = e;

        await s.WriteSettingAsync(PedalSettingId.Smooth, PedalIndex.Brake, new SettingValue(SettingType.UInt8, 40));

        Assert.Equal((PedalSettingId.Smooth, PedalIndex.Brake), (t.LastWrite!.Value.id, t.LastWrite.Value.pedal));
        Assert.NotNull(args);
        Assert.Equal(PedalIndex.Brake, args!.Pedal);
        Assert.Equal(40, args.Value.AsDouble);
    }

    [Fact]
    public void State_Is_Marshalled_To_Handler()
    {
        var t = new FakePedalTransport();
        using var s = Make(t);
        PedalState? got = null;
        s.StateReceived += (_, st) => got = st;

        t.Emit(new PedalState { Throttle = new PedalReading(1, 2) });

        Assert.NotNull(got);
        Assert.Equal((ushort)2, got!.Throttle.Output);
    }
}
