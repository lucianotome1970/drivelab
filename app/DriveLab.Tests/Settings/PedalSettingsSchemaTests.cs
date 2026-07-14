using DriveLab.Core.Settings;

namespace DriveLab.Tests.Settings;

public class PedalSettingsSchemaTests
{
    [Fact]
    public void All_Descriptors_Have_Unique_Ids()
    {
        var ids = PedalSettingsSchema.All.Select(d => d.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(14, ids.Count);
    }

    [Fact]
    public void SensorType_Has_Expected_Metadata()
    {
        var d = PedalSettingsSchema.Get(PedalSettingId.SensorType);
        Assert.Equal(SettingType.UInt8, d.Type);
        Assert.Equal(0, d.Min);
        Assert.Equal(2, d.Max);
        Assert.Equal(0, d.Default);
    }

    [Fact]
    public void CurvePoints_Default_To_Linear()
    {
        Assert.Equal(0, PedalSettingsSchema.Get(PedalSettingId.CurvePoint0).Default);
        Assert.Equal(20, PedalSettingsSchema.Get(PedalSettingId.CurvePoint1).Default);
        Assert.Equal(100, PedalSettingsSchema.Get(PedalSettingId.CurvePoint5).Default);
    }

    [Fact]
    public void CurvePointIds_Are_Six_In_Order()
    {
        Assert.Equal(6, PedalSettingsSchema.CurvePointIds.Length);
        Assert.Equal(PedalSettingId.CurvePoint0, PedalSettingsSchema.CurvePointIds[0]);
        Assert.Equal(PedalSettingId.CurvePoint5, PedalSettingsSchema.CurvePointIds[5]);
    }

    [Fact]
    public void Clamp_Limits_To_Range()
    {
        var d = PedalSettingsSchema.Get(PedalSettingId.Smooth);
        Assert.Equal(0, d.Clamp(-10));
        Assert.Equal(100, d.Clamp(250));
    }

    [Fact]
    public void TryGet_By_FieldId_Finds_Descriptor()
    {
        Assert.True(PedalSettingsSchema.TryGet((byte)PedalSettingId.InputMax, out var d));
        Assert.Equal(PedalSettingId.InputMax, d.Id);
    }
}
