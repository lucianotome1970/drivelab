// ============================================================================
//  DriveLab
//  PedalSettingsSchemaTests.cs — Testes do schema de configurações dos pedais.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

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
        Assert.Equal(3, d.Max);   // 0=Pot, 1=Hall, 2=HX711, 3=célula por amplificador no ADC
        Assert.Equal(0, d.Default);
    }

    /// <summary>
    /// O tipo de sensor é um índice que o firmware interpreta num switch; um máximo apertado
    /// demais no schema faz o app recusar em silêncio um tipo que a placa aceita. Este teste
    /// prende as duas pontas do caminho analógico da célula de carga (`sensor_type == 3`).
    /// </summary>
    [Fact]
    public void SensorType_Accepts_Analog_LoadCell()
    {
        var d = PedalSettingsSchema.Get(PedalSettingId.SensorType);
        Assert.Equal(3, d.Clamp(3));
        Assert.Equal(3, d.Clamp(9));   // acima da faixa continua sendo cortado
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
