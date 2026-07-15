// ============================================================================
//  DriveLab
//  HidHandbrakeTransportTests.cs — Testes de HidHandbrakeTransport.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Hid;
using Xunit;

namespace DriveLab.Hid.Tests;

public class HidHandbrakeTransportTests
{
    private const int ReportSize = 64;

    private static byte[] Frame(byte reportId, byte[] payload64)
    {
        var wire = new byte[1 + ReportSize];
        wire[0] = reportId;
        payload64.CopyTo(wire, 1);
        return wire;
    }

    [Fact]
    public async Task Connect_Opens_Channel()
    {
        var ch = new FakeHidChannel();
        using var t = new HidHandbrakeTransport(ch);
        await t.ConnectAsync();
        Assert.True(t.IsConnected);
        Assert.True(ch.IsOpen);
    }

    [Fact]
    public async Task WriteSetting_Frames_On_Report_0x14_Index0()
    {
        var ch = new FakeHidChannel();
        using var t = new HidHandbrakeTransport(ch);
        await t.ConnectAsync();

        await t.WriteSettingAsync(HandbrakeSettingId.ButtonThreshold, new SettingValue(SettingType.UInt8, 55));

        var wire = ch.LastWrite!;
        Assert.Equal(PedalReportIds.SettingWrite, wire[0]);
        Assert.Equal(0x14, wire[0]);
        var report = SettingReport.Parse(wire.AsSpan(1, ReportSize));
        Assert.Equal((byte)HandbrakeSettingId.ButtonThreshold, report.FieldId);
        Assert.Equal((byte)14, report.FieldId);
        Assert.Equal((byte)0, report.Index);
        Assert.Equal(55, report.Value.AsDouble);
    }

    [Fact]
    public async Task ReadSetting_Correlates_By_Field()
    {
        var ch = new FakeHidChannel();
        using var t = new HidHandbrakeTransport(ch);
        await t.ConnectAsync();

        var task = t.ReadSettingAsync(HandbrakeSettingId.InputMax);
        ch.Emit(Frame(PedalReportIds.SettingValue,
            new SettingReport((byte)HandbrakeSettingId.InputMax, 0,
                new SettingValue(SettingType.UInt16, 3900)).ToBytes()));

        var value = await task;
        Assert.Equal(3900, value.AsDouble);
    }

    [Fact]
    public async Task Telemetry_PedalState_Is_Parsed_And_Raised()
    {
        var ch = new FakeHidChannel();
        using var t = new HidHandbrakeTransport(ch);
        await t.ConnectAsync();
        PedalState? got = null;
        t.StateReceived += (_, s) => got = s;

        var state = new PedalState
        {
            Firmware = new FirmwareVersion(0, 1, 2, 3),
            Brake = new PedalReading(2048, 32768),
        };
        ch.Emit(Frame(PedalReportIds.PedalState, state.ToBytes()));

        Assert.NotNull(got);
        Assert.Equal((ushort)32768, got!.Brake.Output);
        Assert.Equal(new FirmwareVersion(0, 1, 2, 3), t.FirmwareVersion);
    }

    [Fact]
    public async Task SendCommand_Frames_Command_Report()
    {
        var ch = new FakeHidChannel();
        using var t = new HidHandbrakeTransport(ch);
        await t.ConnectAsync();

        await t.SendCommandAsync(PedalCommandId.SaveToFlash);

        var wire = ch.LastWrite!;
        Assert.Equal(PedalReportIds.Command, wire[0]);
        var cmd = CommandReport.Parse(wire.AsSpan(1, ReportSize));
        Assert.Equal((byte)PedalCommandId.SaveToFlash, cmd.CommandId);
    }
}
