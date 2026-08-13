// ============================================================================
//  DriveLab
//  SimulatorHandbrakeTransportTests.cs — Testes do SimulatorHandbrakeTransport.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Simulator;

namespace DriveLab.Tests.Simulator;

public class SimulatorHandbrakeTransportTests
{
    [Fact]
    public async Task Connect_Streams_State()
    {
        var t = new SimulatorHandbrakeTransport();
        PedalState? last = null;
        t.StateReceived += (_, s) => last = s;
        await t.ConnectAsync();
        t.StopStreaming();
        t.SetRawInput(4095);
        t.Step();
        Assert.NotNull(last);
        Assert.Equal((ushort)65535, last!.Clutch.Output);
        await t.DisconnectAsync();
    }

    [Fact]
    public async Task Button_Bit_Set_When_Fully_Pulled()
    {
        var t = new SimulatorHandbrakeTransport();
        PedalState? last = null;
        t.StateReceived += (_, s) => last = s;
        await t.ConnectAsync();
        t.StopStreaming();
        t.SetRawInput(4095);
        t.Step();
        Assert.Equal((byte)HandbrakeFlags.ButtonPressed,
            (byte)(last!.Flags & (byte)HandbrakeFlags.ButtonPressed));
    }

    [Fact]
    public async Task WriteRead_Roundtrips_Setting()
    {
        var t = new SimulatorHandbrakeTransport();
        await t.ConnectAsync();
        await t.WriteSettingAsync(HandbrakeSettingId.ButtonThreshold, new SettingValue(SettingType.UInt8, 55));
        var v = await t.ReadSettingAsync(HandbrakeSettingId.ButtonThreshold);
        Assert.Equal(55, v.AsDouble);
    }
}
