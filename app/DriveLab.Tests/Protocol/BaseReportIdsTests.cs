// ============================================================================
//  DriveLab
//  BaseReportIdsTests.cs — Testes dos valores fixos dos report IDs da base (remapeados p/ a interface HID combinada FFB+A0).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Tests.Protocol;

public class BaseReportIdsTests
{
    [Fact]
    public void ReportIds_Have_Expected_Values()
    {
        // DeviceState/Command foram remapeados de 0x01/0x02 p/ 0x21/0x22 no firmware: a base é UMA
        // interface HID combinada (FFB + A0), e 0x01/0x02 já são usados pelos reports do próprio FFB.
        Assert.Equal(0x21, BaseReportIds.DeviceState);
        Assert.Equal(0x22, BaseReportIds.Command);
        Assert.Equal(0x10, BaseReportIds.DirectControl);
        Assert.Equal(0x14, BaseReportIds.SettingWrite);
        Assert.Equal(0x15, BaseReportIds.SettingReadRequest);
        Assert.Equal(0x16, BaseReportIds.SettingValue);
    }
}
