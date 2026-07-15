// ============================================================================
//  DriveLab
//  BaseSettingsSchemaTests.cs — Testes do schema de configurações do volante.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;

namespace DriveLab.Tests.Settings;

public class BaseSettingsSchemaTests
{
    [Fact]
    public void All_Descriptors_Have_Unique_Ids()
    {
        var ids = BaseSettingsSchema.All.Select(d => d.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void MotionRange_Has_Expected_Metadata()
    {
        var descriptor = BaseSettingsSchema.Get(BaseSettingId.MotionRange);
        Assert.Equal(SettingType.UInt16, descriptor.Type);
        Assert.Equal(90, descriptor.Min);
        Assert.Equal(2000, descriptor.Max);
        Assert.Equal(900, descriptor.Default);
        Assert.Equal(SettingTab.Basic, descriptor.Tab);
    }

    [Fact]
    public void Clamp_Limits_To_Range()
    {
        var descriptor = BaseSettingsSchema.Get(BaseSettingId.TotalStrength);
        Assert.Equal(0, descriptor.Clamp(-50));
        Assert.Equal(100, descriptor.Clamp(250));
        Assert.Equal(60, descriptor.Clamp(60));
    }

    [Fact]
    public void TryGet_By_FieldId_Finds_Descriptor()
    {
        Assert.True(BaseSettingsSchema.TryGet((byte)BaseSettingId.PolePairs, out var descriptor));
        Assert.Equal(BaseSettingId.PolePairs, descriptor.Id);
    }

    [Fact]
    public void TryGet_Unknown_FieldId_Returns_False()
    {
        Assert.False(BaseSettingsSchema.TryGet(250, out _));
    }

    [Fact]
    public void EncoderType_Has_Expected_Metadata()
    {
        var d = BaseSettingsSchema.Get(BaseSettingId.EncoderType);
        Assert.Equal(SettingType.UInt8, d.Type);
        Assert.Equal(0, d.Min);
        Assert.Equal(1, d.Max);
        Assert.Equal(0, d.Default);
        Assert.Equal(SettingTab.Hardware, d.Tab);
    }
}
