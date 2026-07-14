using DriveLab.Core.Protocol;

namespace DriveLab.Tests.Handbrake;

public class HandbrakeIdentityTests
{
    [Fact]
    public void Identity_Uses_Pidcodes_Vid_And_Handbrake_Pid()
    {
        Assert.Equal(0x1209, HandbrakeDeviceIdentity.VendorId);
        Assert.Equal(0x0003, HandbrakeDeviceIdentity.ProductId);
        Assert.Equal(1, HandbrakeDeviceIdentity.ProtocolVersion);
    }

    [Fact]
    public void ButtonPressed_Is_Bit0()
    {
        Assert.Equal((byte)1, (byte)HandbrakeFlags.ButtonPressed);
    }
}
