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
}
