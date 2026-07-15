// ============================================================================
//  DriveLab
//  WheelReportIdsTests.cs — Testes dos valores fixos dos report IDs do rim.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Tests.Protocol;

public class WheelReportIdsTests
{
    [Fact]
    public void ReportIds_Have_Expected_Values()
    {
        Assert.Equal(0x21, WheelReportIds.State);
        Assert.Equal(0x02, WheelReportIds.Command);
        Assert.Equal(0x18, WheelReportIds.Led);
        Assert.Equal(0x14, WheelReportIds.SettingWrite);
        Assert.Equal(0x15, WheelReportIds.SettingReadRequest);
        Assert.Equal(0x16, WheelReportIds.SettingValue);
    }

    [Fact]
    public void SettingId_And_CommandId_Have_Expected_Values()
    {
        Assert.Equal(0, (byte)WheelSettingId.ClutchLeftMin);
        Assert.Equal(7, (byte)WheelSettingId.ClutchBitePoint);
        Assert.Equal(8, (byte)WheelSettingId.LedBrightness);
        Assert.Equal(9, (byte)WheelSettingId.LedCount);
        Assert.Equal(1, (byte)WheelCommandId.CalibrateClutchStart);
        Assert.Equal(3, (byte)WheelCommandId.SaveToFlash);
        Assert.Equal(4, (byte)WheelCommandId.LoadDefaults);
    }
}
