// ============================================================================
//  DriveLab
//  HandbrakeSettingsSchemaTests.cs — Testes do schema de configurações do handbrake.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

// app/DriveLab.Tests/Settings/HandbrakeSettingsSchemaTests.cs
using DriveLab.Core.Settings;

namespace DriveLab.Tests.Settings;

public class HandbrakeSettingsSchemaTests
{
    [Fact]
    public void Field_Ids_0_To_13_Match_Pedal_Numbering()
    {
        Assert.Equal((byte)0, (byte)HandbrakeSettingId.SensorType);
        Assert.Equal((byte)11, (byte)HandbrakeSettingId.LoadCellScale);
        Assert.Equal((byte)13, (byte)HandbrakeSettingId.DeadzoneHigh);
    }

    [Fact]
    public void Adds_Button_Fields_14_And_15()
    {
        Assert.Equal((byte)14, (byte)HandbrakeSettingId.ButtonThreshold);
        Assert.Equal((byte)15, (byte)HandbrakeSettingId.ButtonEnabled);
    }

    [Fact]
    public void Schema_Has_All_16_Descriptors_With_Button_Defaults()
    {
        Assert.Equal(16, HandbrakeSettingsSchema.All.Count);
        Assert.Equal(70, HandbrakeSettingsSchema.Get(HandbrakeSettingId.ButtonThreshold).Default);
        Assert.Equal(1, HandbrakeSettingsSchema.Get(HandbrakeSettingId.ButtonEnabled).Default);
    }

    [Fact]
    public void Get_Clamps_ButtonThreshold_To_0_100()
    {
        var d = HandbrakeSettingsSchema.Get(HandbrakeSettingId.ButtonThreshold);
        Assert.Equal(100, d.Clamp(250));
        Assert.Equal(0, d.Clamp(-5));
    }
}
