// ============================================================================
//  DriveLab
//  WheelReportIdsTests.cs — Testes dos valores fixos dos report IDs do rim.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

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
}
