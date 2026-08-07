// ============================================================================
//  DriveLab
//  EncoderCatalogTests.cs — Testes do catálogo de encoders.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;
using Xunit;

namespace DriveLab.Tests.Settings;

public class EncoderCatalogTests
{
    [Fact]
    public void TodoSensorSuportaAbz()
    {
        // ABZ é o denominador comum: é o caminho já validado no firmware, e todo sensor
        // do catálogo entra por ele sem código novo.
        foreach (var m in EncoderCatalog.Models)
            Assert.Contains(EncoderTech.Abz, EncoderCatalog.TechnologiesFor(m.Id));
    }

    [Fact]
    public void NaoOfereceCombinacaoQueNaoExiste()
    {
        // O AS5047P não tem SSI. Oferecer seria fabricar um erro.
        Assert.DoesNotContain(EncoderTech.Ssi, EncoderCatalog.TechnologiesFor(EncoderCatalog.As5047p));
        // O MT6701 não tem SPI de registrador — o dele é SSI.
        Assert.DoesNotContain(EncoderTech.Spi, EncoderCatalog.TechnologiesFor(EncoderCatalog.Mt6701));
    }

    [Fact]
    public void Mt6835TemAbzESpi()
    {
        var techs = EncoderCatalog.TechnologiesFor(EncoderCatalog.Mt6835);
        Assert.Contains(EncoderTech.Abz, techs);
        Assert.Contains(EncoderTech.Spi, techs);
    }

    [Fact]
    public void ResolucaoDeFabricaEmContagens()
    {
        // O E6B2 da bancada: 2500 PPR × 4 = 10000 contagens.
        Assert.Equal(10000, EncoderCatalog.DefaultResolution(EncoderCatalog.E6b2, EncoderTech.Abz));
        // MT6701 por SSI: 14 bits = 16384 contagens, sem multiplicação.
        Assert.Equal(16384, EncoderCatalog.DefaultResolution(EncoderCatalog.Mt6701, EncoderTech.Ssi));
    }

    [Fact]
    public void GenericoNaoTemResolucaoDeFabrica()
    {
        // No genérico a pessoa digita — o catálogo não pode chutar por ela.
        Assert.Equal(0, EncoderCatalog.DefaultResolution(EncoderCatalog.Generico, EncoderTech.Abz));
    }
}
