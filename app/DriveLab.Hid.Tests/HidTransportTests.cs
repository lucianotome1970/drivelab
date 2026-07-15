// ============================================================================
//  DriveLab
//  HidTransportTests.cs — Testes de HidTransport (conexão, controle direto, comandos e telemetria).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Transport;
using Xunit;

namespace DriveLab.Hid.Tests;

public class HidTransportTests
{
    private static HidTransport New(out FakeHidChannel channel)
    {
        channel = new FakeHidChannel();
        return new HidTransport(channel);
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
        await t.SendDirectControlAsync(new DirectControl { ConstantForce = 5000 });

        Assert.Equal(ReportIds.DirectControl, channel.LastWrite![0]);
        // payload starts at byte 1; DirectControl.ConstantForce is at payload offset 2..3 (int16 LE 5000 = 0x88,0x13)
        Assert.Equal(0x88, channel.LastWrite![1 + 2]);
        Assert.Equal(0x13, channel.LastWrite![1 + 3]);
    }

    [Fact]
    public async Task SendCommand_Writes_Framed_Command_Report()
    {
        var t = New(out var channel);
        await t.ConnectAsync();
        await t.SendCommandAsync(DeviceCommand.ResetCenter, 0);
        Assert.Equal(ReportIds.Command, channel.LastWrite![0]);
        Assert.Equal((byte)DeviceCommand.ResetCenter, channel.LastWrite![1]);
    }

    [Fact]
    public void Incoming_DeviceState_Report_Raises_StateReceived()
    {
        var t = New(out var channel);
        DeviceState? got = null;
        t.StateReceived += (_, s) => got = s;

        var payload = new DeviceState { AngleDeciDeg = 1234, Firmware = new FirmwareVersion(0, 1, 0, 0) }.ToBytes();
        channel.Emit(Wire(ReportIds.DeviceState, payload));

        Assert.NotNull(got);
        Assert.Equal(1234, got!.AngleDeciDeg);
        Assert.Equal(new FirmwareVersion(0, 1, 0, 0), t.FirmwareVersion);
    }

    [Fact]
    public void Malformed_Report_Does_Not_Throw()
    {
        var t = New(out var channel);
        var wire = new byte[1 + 64];
        wire[0] = ReportIds.SettingValue;
        wire[1 + 2] = 0xFF; // invalid SettingType byte in the payload
        var ex = Record.Exception(() => channel.Emit(wire));
        Assert.Null(ex);
    }
}
