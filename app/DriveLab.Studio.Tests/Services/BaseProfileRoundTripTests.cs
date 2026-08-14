// ============================================================================
//  DriveLab
//  BaseProfileRoundTripTests.cs — O perfil de settings da base sobrevive ao arquivo.
//
//  A logica de montar/conferir o perfil e testada em DriveLab.Tests (Core). O que se testa AQUI e a
//  travessia pelo envelope de arquivo, que e onde um formato errado apareceria: o perfil da base e o
//  primeiro a levar um DICIONARIO no lugar de um registro de campos fixos.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using System;
using System.Linq;
using Xunit;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.Tests.Services;

public class BaseProfileRoundTripTests
{
    private static string Exportar(BaseProfile perfil) =>
        ProfileExchange.Serialize(
            BaseProfileExchange.Module,
            new[] { (Name: "Base do Volante", Data: perfil) },
            DateTimeOffset.UnixEpoch);

    [Fact]
    public void Valores_Sobrevivem_A_Ida_E_Volta()
    {
        var original = BaseProfileExchange.Criar(new[]
        {
            (BaseSettingId.MotionRange, 900.0, true),
            (BaseSettingId.MaxTorqueLimit,     8.0, true),
        });

        var lido = ProfileExchange.Deserialize<BaseProfile>(Exportar(original)).Profiles[0].Data!;

        var r = BaseProfileExchange.Preparar(
            lido, new[] { BaseSettingId.MotionRange, BaseSettingId.MaxTorqueLimit }.ToHashSet());

        Assert.Equal(900.0, r.Aplicar[BaseSettingId.MotionRange]);
        Assert.Equal(8.0,   r.Aplicar[BaseSettingId.MaxTorqueLimit]);
        Assert.Equal(0, r.Ajustados);
    }

    /// <summary>O modulo viaja no arquivo justamente para a tela da base recusar um perfil de pedal.</summary>
    [Fact]
    public void Modulo_Identifica_O_Arquivo_Como_Da_Base()
    {
        var json = Exportar(BaseProfileExchange.Criar(new[] { (BaseSettingId.MaxTorqueLimit, 8.0, true) }));

        Assert.Equal(BaseProfileExchange.Module,
                     ProfileExchange.Deserialize<BaseProfile>(json).Module);
    }

    /// <summary>Setting acrescentado depois nao pode invalidar o arquivo de quem exportou antes: o
    /// que a versao atual conhece entra, o resto e contado e ignorado. E a razao de o perfil ser um
    /// dicionario por chave, e nao um registro de campos fixos.</summary>
    [Fact]
    public void Arquivo_De_Outra_Versao_Ainda_Importa_O_Que_Da()
    {
        var json = Exportar(new BaseProfile
        {
            Settings =
            {
                [nameof(BaseSettingId.MaxTorqueLimit)] = 8.0,
                ["SettingDeUmaVersaoFutura"]      = 42.0,
            },
        });

        var lido = ProfileExchange.Deserialize<BaseProfile>(json).Profiles[0].Data!;
        var r = BaseProfileExchange.Preparar(lido, new[] { BaseSettingId.MaxTorqueLimit }.ToHashSet());

        Assert.Equal(8.0, r.Aplicar[BaseSettingId.MaxTorqueLimit]);
        Assert.Equal(new[] { "SettingDeUmaVersaoFutura" }, r.Desconhecidos);
    }
}
