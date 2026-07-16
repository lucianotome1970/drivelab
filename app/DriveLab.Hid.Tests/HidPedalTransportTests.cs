// ============================================================================
//  DriveLab
//  HidPedalTransportTests.cs — Testes de HidPedalTransport.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Hid;
using Xunit;

namespace DriveLab.Hid.Tests;

public class HidPedalTransportTests
{
    private const int ReportSize = 63;

    private static byte[] Frame(byte reportId, byte[] payload64)
    {
        var wire = new byte[1 + ReportSize];
        wire[0] = reportId;
        payload64.CopyTo(wire, 1);
        return wire;
    }

    [Fact]
    public void Is_Configurable_Source()
    {
        using var t = new HidPedalTransport(new FakeHidChannel());
        Assert.True(t.SupportsConfig);
    }

    [Fact]
    public async Task Connect_Opens_Channel()
    {
        var ch = new FakeHidChannel();
        using var t = new HidPedalTransport(ch);
        await t.ConnectAsync();
        Assert.True(t.IsConnected);
        Assert.True(ch.IsOpen);
    }

    [Fact]
    public async Task WriteSetting_Frames_By_ReportId_And_Pedal()
    {
        var ch = new FakeHidChannel();
        using var t = new HidPedalTransport(ch);
        await t.ConnectAsync();

        await t.WriteSettingAsync(PedalSettingId.Smooth, PedalIndex.Brake, new SettingValue(SettingType.UInt8, 40));

        var wire = ch.LastWrite!;
        Assert.Equal(PedalReportIds.SettingWrite, wire[0]);
        var report = SettingReport.Parse(wire.AsSpan(1, ReportSize));
        Assert.Equal((byte)PedalSettingId.Smooth, report.FieldId);
        Assert.Equal((byte)PedalIndex.Brake, report.Index);
        Assert.Equal(40, report.Value.AsDouble);
    }

    [Fact]
    public async Task ReadSetting_Correlates_By_Field_And_Pedal()
    {
        var ch = new FakeHidChannel();
        using var t = new HidPedalTransport(ch);
        await t.ConnectAsync();

        var task = t.ReadSettingAsync(PedalSettingId.InputMax, PedalIndex.Throttle);
        // resposta do device com o MESMO field+index
        ch.Emit(Frame(PedalReportIds.SettingValue,
            new SettingReport((byte)PedalSettingId.InputMax, (byte)PedalIndex.Throttle,
                new SettingValue(SettingType.UInt16, 3900)).ToBytes()));

        var value = await task;
        Assert.Equal(3900, value.AsDouble);
    }

    [Fact]
    public async Task Telemetry_PedalState_Is_Parsed_And_Raised()
    {
        var ch = new FakeHidChannel();
        using var t = new HidPedalTransport(ch);
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
        using var t = new HidPedalTransport(ch);
        await t.ConnectAsync();

        await t.SendCommandAsync(PedalCommandId.SaveToFlash);

        var wire = ch.LastWrite!;
        Assert.Equal(PedalReportIds.Command, wire[0]);
        var cmd = CommandReport.Parse(wire.AsSpan(1, ReportSize));
        Assert.Equal((byte)PedalCommandId.SaveToFlash, cmd.CommandId);
    }
}
