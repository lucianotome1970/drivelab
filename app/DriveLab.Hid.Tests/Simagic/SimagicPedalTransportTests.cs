using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Hid.Simagic;
using Xunit;

namespace DriveLab.Hid.Tests.Simagic;

public class SimagicPedalTransportTests
{
    private static byte[] Report(ushort rx, ushort ry, ushort rz)
    {
        var r = new byte[17];
        r[0] = 0x01;
        r[1] = (byte)(rx & 0xFF); r[2] = (byte)(rx >> 8);
        r[3] = (byte)(ry & 0xFF); r[4] = (byte)(ry >> 8);
        r[5] = (byte)(rz & 0xFF); r[6] = (byte)(rz >> 8);
        return r;
    }

    [Fact]
    public void Is_Read_Only()
    {
        var t = new SimagicPedalTransport(new FakeSimagicReader());
        Assert.False(t.SupportsConfig);
    }

    [Fact]
    public async Task Report_Maps_Rx_Ry_Rz_To_Clutch_Brake_Throttle()
    {
        var reader = new FakeSimagicReader();
        var t = new SimagicPedalTransport(reader);
        await t.ConnectAsync();
        PedalState? got = null;
        t.StateReceived += (_, s) => got = s;

        reader.Emit(Report(rx: 4095, ry: 0, rz: 2048));

        Assert.NotNull(got);
        Assert.Equal((ushort)4095, got!.Clutch.RawInput);
        Assert.Equal((ushort)0, got.Brake.RawInput);
        Assert.Equal((ushort)2048, got.Throttle.RawInput);
        Assert.Equal((ushort)65535, got.Clutch.Output); // curva linear default → fundo
    }

    [Fact]
    public async Task Ignores_Wrong_Report_Id_Or_Short()
    {
        var reader = new FakeSimagicReader();
        var t = new SimagicPedalTransport(reader);
        await t.ConnectAsync();
        var count = 0;
        t.StateReceived += (_, _) => count++;
        reader.Emit(new byte[] { 0x02, 1, 2, 3, 4, 5, 6 }); // id errado
        reader.Emit(new byte[] { 0x01, 1, 2 });              // curto
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SaveToFlash_Is_NoOp_And_Settings_Are_Overlay()
    {
        var reader = new FakeSimagicReader();
        var t = new SimagicPedalTransport(reader);
        await t.ConnectAsync();
        await t.WriteSettingAsync(PedalSettingId.Smooth, PedalIndex.Brake, new SettingValue(SettingType.UInt8, 40));
        Assert.Equal(40, (await t.ReadSettingAsync(PedalSettingId.Smooth, PedalIndex.Brake)).AsDouble);
        await t.SendCommandAsync(PedalCommandId.SaveToFlash); // não lança, não muda nada
        Assert.Equal(40, (await t.ReadSettingAsync(PedalSettingId.Smooth, PedalIndex.Brake)).AsDouble);
    }
}
