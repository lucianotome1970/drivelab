// ============================================================================
//  DriveLab
//  HardwareProfileTests.cs — Testes do perfil de hardware: montagem, validação (barreira de segurança) e
//  round-trip JSON.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Linq;
using DriveLab.Core.Settings;

namespace DriveLab.Tests.Settings;

public class HardwareProfileTests
{
    private static double DefaultOf(BaseSettingId id) => BaseSettingsSchema.Get(id).Default;

    [Fact]
    public void HardwareSettings_Are_The_Hardware_Tab_Only()
    {
        // inclui os perigosos/fixos…
        Assert.Contains(HardwareProfileService.HardwareSettings, d => d.Id == BaseSettingId.BoardVariant);
        Assert.Contains(HardwareProfileService.HardwareSettings, d => d.Id == BaseSettingId.PolePairs);
        Assert.Contains(HardwareProfileService.HardwareSettings, d => d.Id == BaseSettingId.BusNominalV);
        // …e NÃO inclui feel (MotionRange é Basic; TotalStrength é Basic)
        Assert.DoesNotContain(HardwareProfileService.HardwareSettings, d => d.Id == BaseSettingId.MotionRange);
        Assert.DoesNotContain(HardwareProfileService.HardwareSettings, d => d.Id == BaseSettingId.TotalStrength);

        Assert.True(HardwareProfileService.IsHardware(BaseSettingId.BoardVariant));
        Assert.False(HardwareProfileService.IsHardware(BaseSettingId.MotionRange));
    }

    [Fact]
    public void Build_Captures_All_Hardware_Settings_And_Metadata()
    {
        var at = new DateTimeOffset(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);
        var p = HardwareProfileService.Build("Fulano DD", "DD 56V", "notas", at, DefaultOf);

        Assert.Equal(HardwareProfileService.CurrentVersion, p.Version);
        Assert.Equal("hardware-profile", p.Kind);
        Assert.Equal("Fulano DD", p.Vendor);
        Assert.Equal("2026-07-25T12:00:00Z", p.CreatedAt);
        // um valor por setting de hardware, com a Key do schema
        Assert.Equal(HardwareProfileService.HardwareSettings.Count, p.Settings.Count);
        Assert.True(p.Settings.ContainsKey("board_variant"));
        Assert.Equal(BaseSettingsSchema.Get(BaseSettingId.PolePairs).Default, p.Settings["pole_pairs"]);
    }

    [Fact]
    public void Validate_Good_Profile_Has_No_Issues()
    {
        var p = HardwareProfileService.Build("V", "D", "", DateTimeOffset.UnixEpoch, DefaultOf);
        Assert.Empty(HardwareProfileService.Validate(p));
    }

    [Fact]
    public void Validate_Rejects_Out_Of_Range_Value()
    {
        var p = HardwareProfileService.Build("V", "D", "", DateTimeOffset.UnixEpoch, DefaultOf);
        p.Settings["pole_pairs"] = 999;   // fora de [1,50]
        var issues = HardwareProfileService.Validate(p);
        Assert.Contains(issues, s => s.Contains("pole_pairs"));
    }

    [Fact]
    public void Validate_Rejects_Unknown_And_NonHardware_Keys()
    {
        var p = new HardwareProfile { Settings = { ["nao_existe"] = 1, ["total_strength"] = 50 } };
        var issues = HardwareProfileService.Validate(p);
        Assert.Contains(issues, s => s.Contains("desconhecido"));
        Assert.Contains(issues, s => s.Contains("não é de hardware"));
    }

    [Fact]
    public void Validate_Rejects_Wrong_Kind_And_Null()
    {
        Assert.NotEmpty(HardwareProfileService.Validate(null));
        var p = new HardwareProfile { Kind = "feel-profile", Settings = { ["board_variant"] = 1 } };
        Assert.Contains(HardwareProfileService.Validate(p), s => s.Contains("tipo"));
    }

    [Fact]
    public void Serialize_Deserialize_RoundTrips()
    {
        var p = HardwareProfileService.Build("Fulano DD", "DD 56V", "brake 2ohm",
                                             DateTimeOffset.UnixEpoch, DefaultOf);
        var json = HardwareProfileService.Serialize(p);
        // JSON camelCase, keys de settings em snake_case do schema
        Assert.Contains("\"vendor\"", json);
        Assert.Contains("board_variant", json);

        var back = HardwareProfileService.Deserialize(json);
        Assert.NotNull(back);
        Assert.Equal(p.Vendor, back!.Vendor);
        Assert.Equal(p.Settings.Count, back.Settings.Count);
        Assert.Equal(p.Settings["pole_pairs"], back.Settings["pole_pairs"]);
        Assert.Empty(HardwareProfileService.Validate(back));
    }

    [Fact]
    public void Deserialize_Garbage_Returns_Null()
    {
        Assert.Null(HardwareProfileService.Deserialize("{ not json"));
    }
}
