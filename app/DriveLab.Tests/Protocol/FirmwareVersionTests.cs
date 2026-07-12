using DriveLab.Core.Protocol;

namespace DriveLab.Tests.Protocol;

public class FirmwareVersionTests
{
    [Fact]
    public void WriteTo_Then_Parse_RoundTrips()
    {
        var version = new FirmwareVersion(0, 26, 7, 12);
        Span<byte> buffer = stackalloc byte[4];

        version.WriteTo(buffer);
        var parsed = FirmwareVersion.Parse(buffer);

        Assert.Equal(version, parsed);
    }

    [Fact]
    public void WriteTo_Writes_Bytes_In_Order()
    {
        var version = new FirmwareVersion(1, 2, 3, 4);
        Span<byte> buffer = stackalloc byte[4];

        version.WriteTo(buffer);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer.ToArray());
    }
}
