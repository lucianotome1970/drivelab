// ============================================================================
//  DriveLab
//  DeviceStateTests.cs — Testes de round-trip do DeviceState.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Tests.Protocol;

public class DeviceStateTests
{
    [Fact]
    public void ToBytes_Has_ReportSize_Length()
    {
        var state = new DeviceState();
        Assert.Equal(ReportConstants.ReportSize, state.ToBytes().Length);
    }

    [Fact]
    public void ToBytes_Then_Parse_RoundTrips_All_Fields()
    {
        var state = new DeviceState
        {
            Firmware = new FirmwareVersion(0, 26, 7, 12),
            Flags = DeviceFlags.ForceEnabled | DeviceFlags.UsingSimulator,
            Position = -4200,
            AngleDeciDeg = 1350,
            Torque = 9000,
            MotorCurrentMa = -1500,
            FetTempC = 41,
            ErrorCode = 0,
            BusVoltageMv = 23950,
            MotorTempC = 55,
            McuTempC = -128,
        };

        var parsed = DeviceState.Parse(state.ToBytes());

        Assert.Equal(state.Firmware, parsed.Firmware);
        Assert.Equal(state.Flags, parsed.Flags);
        Assert.Equal(state.Position, parsed.Position);
        Assert.Equal(state.AngleDeciDeg, parsed.AngleDeciDeg);
        Assert.Equal(state.Torque, parsed.Torque);
        Assert.Equal(state.MotorCurrentMa, parsed.MotorCurrentMa);
        Assert.Equal(state.FetTempC, parsed.FetTempC);
        Assert.Equal(state.ErrorCode, parsed.ErrorCode);
        Assert.Equal((ushort)23950, parsed.BusVoltageMv);
        Assert.Equal(55, parsed.MotorTempC);
        Assert.Equal(-128, parsed.McuTempC);
    }

    [Fact]
    public void Negative_Int16_Fields_Survive_RoundTrip()
    {
        var state = new DeviceState { Position = -10000, Torque = -10000 };
        var parsed = DeviceState.Parse(state.ToBytes());
        Assert.Equal(-10000, parsed.Position);
        Assert.Equal(-10000, parsed.Torque);
    }

    [Fact]
    public void BusVoltage_Above_Int16Max_Survives_RoundTrip()
    {
        // Guards the u16 signedness path: values > 32767 must not wrap negative.
        var state = new DeviceState { BusVoltageMv = 65535 };
        var parsed = DeviceState.Parse(state.ToBytes());
        Assert.Equal((ushort)65535, parsed.BusVoltageMv);
    }
}
