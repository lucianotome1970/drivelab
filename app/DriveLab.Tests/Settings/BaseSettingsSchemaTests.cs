// ============================================================================
//  DriveLab
//  BaseSettingsSchemaTests.cs — Testes do schema de configurações do volante.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Linq;
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
        // Teto do PROTOCOLO: o eixo que vai para o jogo representa ±540° (1080 no total). Acima
        // disso o volante passa do que o relatório consegue dizer e o jogo vê o eixo travado.
        Assert.Equal(1080, descriptor.Max);
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
    public void EncoderType_Bid_Matches_Firmware()
    {
        Assert.Equal(18, (byte)BaseSettingId.EncoderType);
    }

    [Fact]
    public void EncoderType_Has_Expected_Metadata()
    {
        var d = BaseSettingsSchema.Get(BaseSettingId.EncoderType);
        Assert.Equal(SettingType.UInt8, d.Type);
        Assert.Equal(0, d.Min);
        // O limite acompanha o CATÁLOGO, não um número fixo: acrescentar um sensor lá não pode
        // deixar o campo sem conseguir representá-lo.
        Assert.Equal(EncoderCatalog.Models.Max(m => m.Id), d.Max);
        // Padrao = E6B2, o incremental da bancada. Nao ha mais opcao "generico": escolher pelo
        // nome e o que deixa a tela saber quais interfaces o sensor tem e de onde vem a resolucao.
        Assert.Equal(EncoderCatalog.E6b2, d.Default);
        Assert.Equal(SettingTab.Hardware, d.Tab);
    }

    [Fact]
    public void SoftPowerEnable_Bid_Matches_Firmware()
    {
        Assert.Equal(43, (byte)BaseSettingId.SoftPowerEnable);
    }

    [Fact]
    public void SoftPowerEnable_Has_Expected_Metadata()
    {
        var d = BaseSettingsSchema.Get(BaseSettingId.SoftPowerEnable);
        Assert.Equal(SettingType.UInt8, d.Type);
        Assert.Equal(0, d.Min);
        Assert.Equal(1, d.Max);
        Assert.Equal(0, d.Default);
        Assert.Equal(SettingTab.Hardware, d.Tab);
    }

    [Fact]
    public void PowerButtonEnable_Bid_Matches_Firmware()
    {
        Assert.Equal(44, (byte)BaseSettingId.PowerButtonEnable);
    }

    [Fact]
    public void PowerButtonEnable_Has_Expected_Metadata()
    {
        var d = BaseSettingsSchema.Get(BaseSettingId.PowerButtonEnable);
        Assert.Equal(SettingType.UInt8, d.Type);
        Assert.Equal(0, d.Min);
        Assert.Equal(1, d.Max);
        Assert.Equal(0, d.Default);
        Assert.Equal(SettingTab.Hardware, d.Tab);
    }

    [Theory]
    [InlineData(BaseSettingId.SpringGain, 39)]
    [InlineData(BaseSettingId.DamperGain, 40)]
    [InlineData(BaseSettingId.FrictionGain, 41)]
    [InlineData(BaseSettingId.InertiaGain, 42)]
    public void GainSetting_Bid_Matches_Firmware(BaseSettingId id, byte expectedBid)
    {
        Assert.Equal(expectedBid, (byte)id);
    }

    [Theory]
    [InlineData(BaseSettingId.SpringGain)]
    [InlineData(BaseSettingId.DamperGain)]
    [InlineData(BaseSettingId.FrictionGain)]
    [InlineData(BaseSettingId.InertiaGain)]
    public void GainSetting_Has_Expected_Metadata(BaseSettingId id)
    {
        var d = BaseSettingsSchema.Get(id);
        Assert.Equal(SettingType.UInt8, d.Type);
        Assert.Equal(0, d.Min);
        Assert.Equal(200, d.Max);
        Assert.Equal(100, d.Default);
        Assert.Equal(SettingTab.Feel, d.Tab);
    }
}
