using Xunit;

namespace DriveLab.Hid.Tests;

public class FakeHidChannelTests
{
    [Fact]
    public async Task Open_Write_Emit_Work()
    {
        var channel = new FakeHidChannel();
        Assert.True(await channel.OpenAsync(1, 2));
        Assert.True(channel.IsOpen);

        await channel.WriteAsync(new byte[] { 0x10, 1, 2, 3 });
        Assert.Equal(new byte[] { 0x10, 1, 2, 3 }, channel.LastWrite);

        byte[]? got = null;
        channel.ReportReceived += (_, r) => got = r;
        channel.Emit(new byte[] { 0x01, 9 });
        Assert.Equal(new byte[] { 0x01, 9 }, got);
    }
}
