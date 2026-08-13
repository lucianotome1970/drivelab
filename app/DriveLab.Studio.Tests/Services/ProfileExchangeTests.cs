// ============================================================================
//  DriveLab
//  ProfileExchangeTests.cs — Testes do envelope de exportar/importar perfis (round-trip, colisão, versão).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using Xunit;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.Tests.Services;

public class ProfileExchangeTests
{
    private sealed class FakeProfile
    {
        public int Strength { get; set; }
        public string Label { get; set; } = "";
    }

    [Fact]
    public void Serialize_Then_Deserialize_RoundTrips()
    {
        var json = ProfileExchange.Serialize("wheel", new[]
        {
            ("GT3", new FakeProfile { Strength = 80, Label = "seco" }),
            ("Chuva", new FakeProfile { Strength = 55, Label = "molhado" }),
        }, DateTimeOffset.UnixEpoch);

        var env = ProfileExchange.Deserialize<FakeProfile>(json);

        Assert.Equal(ProfileExchange.CurrentVersion, env.Version);
        Assert.Equal("wheel", env.Module);
        Assert.Equal(2, env.Profiles.Count);
        Assert.Equal("GT3", env.Profiles[0].Name);
        Assert.Equal(80, env.Profiles[0].Data!.Strength);
        Assert.Equal("molhado", env.Profiles[1].Data!.Label);
    }

    [Fact]
    public void UniqueName_SuffixesOnCollision_NeverOverwrites()
    {
        var existing = new[] { "GT3", "GT3 (2)" };

        Assert.Equal("Chuva", ProfileExchange.UniqueName(existing, "Chuva"));
        Assert.Equal("GT3 (3)", ProfileExchange.UniqueName(existing, "GT3"));
        Assert.Equal("Importado", ProfileExchange.UniqueName(existing, "   "));
    }

    [Fact]
    public void Deserialize_Rejects_InvalidJson()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProfileExchange.Deserialize<FakeProfile>("isto não é json"));
        Assert.Contains("inválido", ex.Message);
    }

    [Fact]
    public void Deserialize_Rejects_FutureVersion()
    {
        var json = $$"""{"Version":{{ProfileExchange.CurrentVersion + 1}},"Module":"wheel","Profiles":[]}""";

        var ex = Assert.Throws<InvalidOperationException>(
            () => ProfileExchange.Deserialize<FakeProfile>(json));
        Assert.Contains("mais nova", ex.Message);
    }
}
