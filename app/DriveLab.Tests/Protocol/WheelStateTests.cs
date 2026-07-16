// ============================================================================
//  DriveLab
//  WheelStateTests.cs — Testes de round-trip do WheelState (telemetria do rim).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Tests.Protocol;

public class WheelStateTests
{
    [Fact]
    public void ToBytes_Has_ReportSize_Length()
    {
        Assert.Equal(ReportConstants.ReportSize, new WheelState().ToBytes().Length);
    }

    [Fact]
    public void ToBytes_Then_Parse_RoundTrips_All_Fields()
    {
        var state = new WheelState
        {
            Firmware = new FirmwareVersion(0, 1, 0, 0),
            Flags = WheelFlags.Calibrated | WheelFlags.UsingSimulator,
            Buttons = 0b1010_0000_0000_0000_0000_0000_0000_0101u,
            ClutchLeft = new WheelAxis(1234, 2345),
            ClutchRight = new WheelAxis(3456, 4095),
            EncoderDeltas = new sbyte[] { 3, -2, 0, 127, -5 },
        };

        var parsed = WheelState.Parse(state.ToBytes());

        Assert.Equal(state.Firmware, parsed.Firmware);
        Assert.Equal(state.Flags, parsed.Flags);
        Assert.Equal(state.Buttons, parsed.Buttons);
        Assert.Equal(state.ClutchLeft, parsed.ClutchLeft);
        Assert.Equal(state.ClutchRight, parsed.ClutchRight);
        Assert.Equal(state.EncoderDeltas, parsed.EncoderDeltas);
    }

    [Fact]
    public void IsButtonPressed_Reads_The_Right_Bit()
    {
        var state = new WheelState { Buttons = 1u << 5 };
        Assert.True(state.IsButtonPressed(5));
        Assert.False(state.IsButtonPressed(4));
        Assert.False(state.IsButtonPressed(31));
    }

    [Fact]
    public void High_Bit_Buttons_Survive_RoundTrip()
    {
        // Guarda o caminho u32: bit 31 não pode se perder / virar sinal.
        var state = new WheelState { Buttons = 1u << 31 };
        var parsed = WheelState.Parse(state.ToBytes());
        Assert.Equal(1u << 31, parsed.Buttons);
        Assert.True(parsed.IsButtonPressed(31));
    }
}
