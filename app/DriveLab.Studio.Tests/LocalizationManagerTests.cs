// ============================================================================
//  DriveLab
//  LocalizationManagerTests.cs — Testes de LocalizationManager (troca de idioma e chaves ausentes).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Globalization;
using DriveLab.Studio.Localization;
using Xunit;

namespace DriveLab.Studio.Tests;

[Collection("Loc")]
public class LocalizationManagerTests
{
    [Fact]
    public void English_By_Default()
    {
        LocalizationManager.UseCulture(new CultureInfo("en-US"));
        Assert.False(LocalizationManager.IsPortuguese);
        Assert.Equal("Connect", LocalizationManager.Get("Connect"));
        Assert.Equal("Wheel Base", LocalizationManager.Get("Nav_WheelBase"));
        Assert.Equal("Total force", LocalizationManager.Get("Setting_TotalStrength"));
        Assert.Equal("Quadrature (E6B2)", LocalizationManager.Get("Setting_EncoderType_Quadrature"));
        Assert.Equal("Magnetic SPI (AS5047)", LocalizationManager.Get("Setting_EncoderType_MagneticSPI"));
    }

    [Fact]
    public void Portuguese_When_Pt_Culture()
    {
        LocalizationManager.UseCulture(new CultureInfo("pt-BR"));
        Assert.True(LocalizationManager.IsPortuguese);
        Assert.Equal("Conectar", LocalizationManager.Get("Connect"));
        Assert.Equal("Base do Volante", LocalizationManager.Get("Nav_WheelBase"));
        Assert.Equal("Força total", LocalizationManager.Get("Setting_TotalStrength"));
        Assert.Equal("Quadratura (E6B2)", LocalizationManager.Get("Setting_EncoderType_Quadrature"));
        Assert.Equal("SPI magnético (AS5047)", LocalizationManager.Get("Setting_EncoderType_MagneticSPI"));
    }

    [Fact]
    public void Missing_Key_Falls_Back_To_The_Key()
    {
        Assert.Equal("Zzz_Missing_Key", LocalizationManager.Get("Zzz_Missing_Key"));
    }
}
