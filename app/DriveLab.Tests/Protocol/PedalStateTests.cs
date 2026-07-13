using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Tests.Protocol;

public class PedalStateTests
{
    [Fact]
    public void ReportIds_And_Enums_Have_Expected_Values()
    {
        Assert.Equal(0x20, PedalReportIds.PedalState);
        Assert.Equal(0x14, PedalReportIds.SettingWrite);
        Assert.Equal(0x16, PedalReportIds.SettingValue);
        Assert.Equal((byte)0, (byte)PedalIndex.Clutch);
        Assert.Equal((byte)1, (byte)PedalIndex.Brake);
        Assert.Equal((byte)2, (byte)PedalIndex.Throttle);
        Assert.Equal((byte)3, (byte)PedalCommandId.SaveToFlash);
    }

    [Fact]
    public void PedalState_RoundTrips_All_Fields()
    {
        var state = new PedalState
        {
            Firmware = new FirmwareVersion(0, 1, 2, 3),
            Flags = 0b0000_0001,
            Clutch = new PedalReading(100, 200),
            Brake = new PedalReading(4095, 65535),
            Throttle = new PedalReading(0, 32768),
        };

        var bytes = state.ToBytes();
        Assert.Equal(64, bytes.Length);

        var parsed = PedalState.Parse(bytes);
        Assert.Equal(new FirmwareVersion(0, 1, 2, 3), parsed.Firmware);
        Assert.Equal(1, parsed.Flags);
        Assert.Equal(new PedalReading(100, 200), parsed.Clutch);
        Assert.Equal(new PedalReading(4095, 65535), parsed.Brake);
        Assert.Equal(new PedalReading(0, 32768), parsed.Throttle);
    }

    [Fact]
    public void PedalState_Indexer_Returns_Right_Pedal()
    {
        var state = new PedalState
        {
            Clutch = new PedalReading(1, 1),
            Brake = new PedalReading(2, 2),
            Throttle = new PedalReading(3, 3),
        };
        Assert.Equal(new PedalReading(2, 2), state[PedalIndex.Brake]);
        Assert.Equal(new PedalReading(3, 3), state[PedalIndex.Throttle]);
    }
}
