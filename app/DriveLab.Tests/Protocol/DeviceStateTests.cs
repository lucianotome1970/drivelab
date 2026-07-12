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
            TemperatureC = 37,
            ErrorCode = 0,
        };

        var parsed = DeviceState.Parse(state.ToBytes());

        Assert.Equal(state.Firmware, parsed.Firmware);
        Assert.Equal(state.Flags, parsed.Flags);
        Assert.Equal(state.Position, parsed.Position);
        Assert.Equal(state.AngleDeciDeg, parsed.AngleDeciDeg);
        Assert.Equal(state.Torque, parsed.Torque);
        Assert.Equal(state.MotorCurrentMa, parsed.MotorCurrentMa);
        Assert.Equal(state.TemperatureC, parsed.TemperatureC);
        Assert.Equal(state.ErrorCode, parsed.ErrorCode);
    }

    [Fact]
    public void Negative_Int16_Fields_Survive_RoundTrip()
    {
        var state = new DeviceState { Position = -10000, Torque = -10000 };
        var parsed = DeviceState.Parse(state.ToBytes());
        Assert.Equal(-10000, parsed.Position);
        Assert.Equal(-10000, parsed.Torque);
    }
}
