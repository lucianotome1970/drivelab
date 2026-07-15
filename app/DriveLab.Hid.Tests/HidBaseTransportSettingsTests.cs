// ============================================================================
//  DriveLab
//  HidTransportSettingsTests.cs — Testes de leitura/escrita de settings via HidTransport.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using Xunit;

namespace DriveLab.Hid.Tests;

public class HidTransportSettingsTests
{
    private static byte[] Wire(byte reportId, byte[] payload64)
    {
        var wire = new byte[1 + payload64.Length];
        wire[0] = reportId;
        payload64.CopyTo(wire, 1);
        return wire;
    }

    [Fact]
    public async Task WriteSetting_Sends_SettingWrite_Report()
    {
        var channel = new FakeHidChannel();
        var t = new HidTransport(channel);
        await t.ConnectAsync();

        await t.WriteSettingAsync(BaseSettingId.MotionRange, new SettingValue(SettingType.UInt16, 900));

        Assert.Equal(BaseReportIds.SettingWrite, channel.LastWrite![0]);
        var payload = new byte[64];
        Array.Copy(channel.LastWrite!, 1, payload, 0, 64);
        var report = SettingReport.Parse(payload);
        Assert.Equal((byte)BaseSettingId.MotionRange, report.FieldId);
        Assert.Equal(900, report.Value.AsDouble);
    }

    [Fact]
    public async Task ReadSetting_Sends_Request_And_Completes_On_Reply()
    {
        var channel = new FakeHidChannel();
        var t = new HidTransport(channel);
        await t.ConnectAsync();

        var readTask = t.ReadSettingAsync(BaseSettingId.EncoderCpr);

        // firmware would answer with a SettingValue report for the same field
        var reply = new SettingReport((byte)BaseSettingId.EncoderCpr, 0, new SettingValue(SettingType.UInt16, 10000)).ToBytes();
        channel.Emit(Wire(BaseReportIds.SettingValue, reply));

        var value = await readTask;
        Assert.Equal(SettingType.UInt16, value.Type);
        Assert.Equal(10000, value.AsDouble);

        // and it sent a read request first
        Assert.Equal(BaseReportIds.SettingReadRequest, channel.Writes[0][0]);
    }

    [Fact]
    public async Task ReadSetting_Times_Out_When_No_Reply()
    {
        var channel = new FakeHidChannel();
        var t = new HidTransport(channel);
        await t.ConnectAsync();

        await Assert.ThrowsAsync<TimeoutException>(() => t.ReadSettingAsync(BaseSettingId.PolePairs, TimeSpan.FromMilliseconds(50)));
    }
}
