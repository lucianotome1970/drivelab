// ============================================================================
//  DriveLab
//  WireReportsTests.cs — Testes dos relatórios de wire (SettingReport, CommandReport etc.).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Tests.Protocol;

public class WireReportsTests
{
    [Fact]
    public void ReportIds_And_Identity_Have_Expected_Values()
    {
        Assert.Equal(0x01, BaseReportIds.DeviceState);
        Assert.Equal(0x14, BaseReportIds.SettingWrite);
        Assert.Equal(0x16, BaseReportIds.SettingValue);
        Assert.Equal(0x1209, BaseDeviceIdentity.VendorId);
        Assert.Equal(1, BaseDeviceIdentity.ProtocolVersion);
    }

    [Fact]
    public void SettingReport_RoundTrips_Uint16()
    {
        var report = new SettingReport((byte)BaseSettingId.MotionRange, 0, new SettingValue(SettingType.UInt16, 900));
        var bytes = report.ToBytes();
        Assert.Equal(ReportConstants.ReportSize, bytes.Length);

        var parsed = SettingReport.Parse(bytes);
        Assert.Equal((byte)BaseSettingId.MotionRange, parsed.FieldId);
        Assert.Equal(0, parsed.Index);
        Assert.Equal(SettingType.UInt16, parsed.Value.Type);
        Assert.Equal(900, parsed.Value.AsDouble);
    }

    [Fact]
    public void SettingReport_RoundTrips_Float()
    {
        var report = new SettingReport((byte)BaseSettingId.CurrentP, 0, new SettingValue(SettingType.Float, 0.05));
        var parsed = SettingReport.Parse(report.ToBytes());
        Assert.Equal(SettingType.Float, parsed.Value.Type);
        Assert.Equal(0.05, parsed.Value.AsDouble, precision: 5);
    }

    [Fact]
    public void SettingReadRequestReport_RoundTrips()
    {
        var report = new SettingReadRequestReport((byte)BaseSettingId.EncoderCpr, 0);
        var parsed = SettingReadRequestReport.Parse(report.ToBytes());
        Assert.Equal(ReportConstants.ReportSize, report.ToBytes().Length);
        Assert.Equal((byte)BaseSettingId.EncoderCpr, parsed.FieldId);
        Assert.Equal(0, parsed.Index);
    }

    [Fact]
    public void CommandReport_RoundTrips()
    {
        var report = new CommandReport((byte)BaseCommand.SetForceEnabled, 1);
        var parsed = CommandReport.Parse(report.ToBytes());
        Assert.Equal((byte)BaseCommand.SetForceEnabled, parsed.CommandId);
        Assert.Equal(1, parsed.Arg);
    }
}
