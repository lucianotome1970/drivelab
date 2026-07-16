// ============================================================================
//  DriveLab
//  HidWheelTransportTests.cs — Testes de HidWheelTransport (settings, comandos, LED e telemetria).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Hid;
using Xunit;

namespace DriveLab.Hid.Tests;

public class HidWheelTransportTests
{
    private const int ReportSize = 63;

    private static byte[] Frame(byte reportId, byte[] payload63)
    {
        var wire = new byte[1 + ReportSize];
        wire[0] = reportId;
        payload63.CopyTo(wire, 1);
        return wire;
    }

    [Fact]
    public async Task Connect_Opens_Channel()
    {
        var ch = new FakeHidChannel();
        using var t = new HidWheelTransport(ch);
        await t.ConnectAsync();
        Assert.True(t.IsConnected);
        Assert.True(ch.IsOpen);
    }

    [Fact]
    public async Task WriteSetting_Frames_On_Report_0x14_Index0()
    {
        var ch = new FakeHidChannel();
        using var t = new HidWheelTransport(ch);
        await t.ConnectAsync();

        await t.WriteSettingAsync(WheelSettingId.LedBrightness, new SettingValue(SettingType.UInt8, 200));

        var wire = ch.LastWrite!;
        Assert.Equal(WheelReportIds.SettingWrite, wire[0]);
        Assert.Equal(0x14, wire[0]);
        var report = SettingReport.Parse(wire.AsSpan(1, ReportSize));
        Assert.Equal((byte)WheelSettingId.LedBrightness, report.FieldId);
        Assert.Equal((byte)8, report.FieldId);
        Assert.Equal((byte)0, report.Index);
        Assert.Equal(200, report.Value.AsDouble);
    }

    [Fact]
    public async Task ReadSetting_Correlates_By_Field()
    {
        var ch = new FakeHidChannel();
        using var t = new HidWheelTransport(ch);
        await t.ConnectAsync();

        var task = t.ReadSettingAsync(WheelSettingId.ClutchBitePoint);
        ch.Emit(Frame(WheelReportIds.SettingValue,
            new SettingReport((byte)WheelSettingId.ClutchBitePoint, 0,
                new SettingValue(SettingType.UInt8, 45)).ToBytes()));

        var value = await task;
        Assert.Equal(45, value.AsDouble);
    }

    [Fact]
    public async Task Telemetry_WheelState_Is_Parsed_And_Raised()
    {
        var ch = new FakeHidChannel();
        using var t = new HidWheelTransport(ch);
        await t.ConnectAsync();
        WheelState? got = null;
        t.StateReceived += (_, s) => got = s;

        var state = new WheelState
        {
            Firmware = new FirmwareVersion(0, 1, 0, 0),
            Buttons = 0b101,                              // botões 0 e 2 pressionados
            ClutchLeft = new WheelAxis(2048, 40000),
        };
        ch.Emit(Frame(WheelReportIds.State, state.ToBytes()));

        Assert.NotNull(got);
        Assert.True(got!.IsButtonPressed(0));
        Assert.True(got.IsButtonPressed(2));
        Assert.False(got.IsButtonPressed(1));
        Assert.Equal((ushort)40000, got.ClutchLeft.Output);
        Assert.Equal(new FirmwareVersion(0, 1, 0, 0), t.FirmwareVersion);
    }

    [Fact]
    public async Task SendCommand_Frames_On_Report_0x02()
    {
        var ch = new FakeHidChannel();
        using var t = new HidWheelTransport(ch);
        await t.ConnectAsync();

        await t.SendCommandAsync(WheelCommandId.SaveToFlash);

        var wire = ch.LastWrite!;
        Assert.Equal(WheelReportIds.Command, wire[0]);
        var cmd = CommandReport.Parse(wire.AsSpan(1, ReportSize));
        Assert.Equal((byte)WheelCommandId.SaveToFlash, cmd.CommandId);
    }

    [Fact]
    public async Task SendLed_Frames_On_Report_0x18_With_Colors()
    {
        var ch = new FakeHidChannel();
        using var t = new HidWheelTransport(ch);
        await t.ConnectAsync();

        var led = new WheelLedReport(180, new[]
        {
            new WheelLedColor(255, 0, 0),
            new WheelLedColor(0, 255, 0),
        });
        await t.SendLedAsync(led);

        var wire = ch.LastWrite!;
        Assert.Equal(WheelReportIds.Led, wire[0]);
        Assert.Equal(0x18, wire[0]);
        var parsed = WheelLedReport.Parse(wire.AsSpan(1, ReportSize));
        Assert.Equal((byte)180, parsed.Brightness);
        Assert.Equal(2, parsed.Leds.Count);
        Assert.Equal(new WheelLedColor(255, 0, 0), parsed.Leds[0]);
        Assert.Equal(new WheelLedColor(0, 255, 0), parsed.Leds[1]);
    }
}
