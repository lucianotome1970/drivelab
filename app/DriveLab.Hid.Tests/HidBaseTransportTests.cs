// ============================================================================
//  DriveLab
//  HidBaseTransportTests.cs — Testes de HidBaseTransport (conexão, controle direto, comandos e telemetria).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Transport;
using Xunit;

namespace DriveLab.Hid.Tests;

public class HidBaseTransportTests
{
    private static HidBaseTransport New(out FakeHidChannel channel)
    {
        channel = new FakeHidChannel();
        return new HidBaseTransport(channel);
    }

    private static byte[] Wire(byte reportId, byte[] payload64)
    {
        var wire = new byte[1 + payload64.Length];
        wire[0] = reportId;
        payload64.CopyTo(wire, 1);
        return wire;
    }

    [Fact]
    public async Task ConnectAsync_Opens_Channel()
    {
        var t = New(out var channel);
        await t.ConnectAsync();
        Assert.True(t.IsConnected);
        Assert.True(channel.IsOpen);
    }

    [Fact]
    public async Task SendDirectControl_Writes_Framed_Report()
    {
        var t = New(out var channel);
        await t.ConnectAsync();
        await t.SendDirectControlAsync(new BaseDirectControl { ConstantForce = 5000 });

        Assert.Equal(BaseReportIds.DirectControl, channel.LastWrite![0]);
        // payload starts at byte 1; BaseDirectControl.ConstantForce is at payload offset 2..3 (int16 LE 5000 = 0x88,0x13)
        Assert.Equal(0x88, channel.LastWrite![1 + 2]);
        Assert.Equal(0x13, channel.LastWrite![1 + 3]);
    }

    [Fact]
    public async Task SendCommand_Writes_Framed_Command_Report()
    {
        var t = New(out var channel);
        await t.ConnectAsync();
        await t.SendCommandAsync(BaseCommand.ResetCenter, 0);
        Assert.Equal(BaseReportIds.Command, channel.LastWrite![0]);
        Assert.Equal((byte)BaseCommand.ResetCenter, channel.LastWrite![1]);
    }

    [Fact]
    public void Incoming_DeviceState_Report_Raises_StateReceived()
    {
        var t = New(out var channel);
        BaseState? got = null;
        t.StateReceived += (_, s) => got = s;

        var payload = new BaseState { AngleDeciDeg = 1234, Firmware = new FirmwareVersion(0, 1, 0, 0) }.ToBytes();
        channel.Emit(Wire(BaseReportIds.DeviceState, payload));

        Assert.NotNull(got);
        Assert.Equal(1234, got!.AngleDeciDeg);
        Assert.Equal(new FirmwareVersion(0, 1, 0, 0), t.FirmwareVersion);
    }

    [Fact]
    public void Malformed_Report_Does_Not_Throw()
    {
        var t = New(out var channel);
        var wire = new byte[1 + 63];
        wire[0] = BaseReportIds.SettingValue;
        wire[1 + 2] = 0xFF; // invalid SettingType byte in the payload
        var ex = Record.Exception(() => channel.Emit(wire));
        Assert.Null(ex);
    }

    // The base ships as ONE combined HID interface with two top-level collections: Generic-Desktop
    // 0x01 (the FFB wheel) and vendor 0xFF00 (A0 config/telemetry). HidSharp enumerates one HidDevice
    // per top-level collection for VID 0x1209/PID 0x0001, so the transport must pick the 0xFF00 one.
    [Theory]
    [InlineData(0xFF00, true)]
    [InlineData(0x01, false)]  // Generic Desktop (FFB collection) — must NOT be selected
    [InlineData(0x00, false)]
    [InlineData(0xFF01, false)]
    public void IsA0UsagePage_True_Only_For_Vendor_0xFF00(int usagePage, bool expected)
    {
        Assert.Equal(expected, HidBaseTransport.IsA0UsagePage(usagePage));
    }
}
