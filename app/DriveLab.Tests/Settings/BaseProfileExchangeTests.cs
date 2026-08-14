// ============================================================================
//  DriveLab
//  BaseProfileExchangeTests.cs — Testes do perfil de settings da base (exportar/importar arquivo).
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using System.Collections.Generic;
using System.Linq;
using DriveLab.Core.Settings;

namespace DriveLab.Tests.Settings;

public class BaseProfileExchangeTests
{
    private static IReadOnlySet<BaseSettingId> Editaveis(params BaseSettingId[] ids) => ids.ToHashSet();

    // ---------------------------------------------------------------------------------------
    // Exportar
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Criar_Exporta_Campos_Lidos()
    {
        var perfil = BaseProfileExchange.Criar(new[]
        {
            (BaseSettingId.MotionRange, 900.0, true),
            (BaseSettingId.MaxTorqueLimit,     8.0, true),
        });

        Assert.Equal(2, perfil.Settings.Count);
        Assert.Equal(900.0, perfil.Settings[nameof(BaseSettingId.MotionRange)]);
    }

    /// <summary>Campo que ainda nao carregou da base exibe "—" mas guarda o padrao do schema por
    /// dentro. Exportar isso gravaria um valor que a placa nunca teve, e na volta ele seria aplicado
    /// como se fosse escolha de alguem.</summary>
    [Fact]
    public void Criar_Ignora_Campo_Nao_Lido()
    {
        var perfil = BaseProfileExchange.Criar(new[]
        {
            (BaseSettingId.MotionRange, 900.0, true),
            (BaseSettingId.MaxTorqueLimit,     8.0, false),
        });

        Assert.Single(perfil.Settings);
        Assert.False(perfil.Settings.ContainsKey(nameof(BaseSettingId.MaxTorqueLimit)));
    }

    // ---------------------------------------------------------------------------------------
    // Importar
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Preparar_Aplica_O_Que_Existe_E_E_Editavel()
    {
        var perfil = new BaseProfile
        {
            Settings = { [nameof(BaseSettingId.MotionRange)] = 540.0 },
        };

        var r = BaseProfileExchange.Preparar(perfil, Editaveis(BaseSettingId.MotionRange));

        Assert.Equal(540.0, r.Aplicar[BaseSettingId.MotionRange]);
        Assert.Empty(r.Desconhecidos);
        Assert.Empty(r.ForaDestaTela);
        Assert.Equal(0, r.Ajustados);
    }

    /// <summary>O arquivo e texto e pode ser editado a mao. Valor fora de faixa em corrente ou ganho
    /// nao pode chegar cru ao firmware — importar nao pode produzir um estado que a tela nao produz.</summary>
    [Fact]
    public void Preparar_Limita_Valor_Fora_De_Faixa_E_Conta()
    {
        var d = BaseSettingsSchema.Get(BaseSettingId.MotionRange);
        var perfil = new BaseProfile
        {
            Settings = { [nameof(BaseSettingId.MotionRange)] = d.Max + 10_000 },
        };

        var r = BaseProfileExchange.Preparar(perfil, Editaveis(BaseSettingId.MotionRange));

        Assert.Equal(d.Max, r.Aplicar[BaseSettingId.MotionRange]);
        Assert.Equal(1, r.Ajustados);
    }

    /// <summary>Modo cliente: a aba Hardware nao existe, entao os campos que descrevem a MAQUINA nao
    /// podem entrar vindos do arquivo de outra pessoa — e a tela precisa CONTAR que ficaram de fora.</summary>
    [Fact]
    public void Preparar_Separa_Campo_Que_A_Tela_Nao_Edita()
    {
        var perfil = new BaseProfile
        {
            Settings =
            {
                [nameof(BaseSettingId.MotionRange)] = 540.0,
                [nameof(BaseSettingId.PolePairs)]   = 15.0,
            },
        };

        var r = BaseProfileExchange.Preparar(perfil, Editaveis(BaseSettingId.MotionRange));

        Assert.Single(r.Aplicar);
        Assert.Equal(new[] { nameof(BaseSettingId.PolePairs) }, r.ForaDestaTela);
    }

    /// <summary>Arquivo de uma versao mais nova (ou editado a mao) traz chave que este app nao conhece.
    /// Ignorar UMA nao pode derrubar as outras — mas tem de aparecer na contagem.</summary>
    [Fact]
    public void Preparar_Ignora_Chave_Desconhecida_Sem_Perder_As_Boas()
    {
        var perfil = new BaseProfile
        {
            Settings =
            {
                ["SettingQueNaoExiste"]             = 1.0,
                [nameof(BaseSettingId.MotionRange)] = 540.0,
            },
        };

        var r = BaseProfileExchange.Preparar(perfil, Editaveis(BaseSettingId.MotionRange));

        Assert.Single(r.Aplicar);
        Assert.Equal(new[] { "SettingQueNaoExiste" }, r.Desconhecidos);
    }

    /// <summary>Numero cru no lugar do nome nao pode virar setting por acidente: "12" converteria
    /// para o enum de valor 12 sem nenhum aviso, e o arquivo passaria a mexer num campo que nao
    /// nomeou. So nome vale.</summary>
    [Fact]
    public void Preparar_Recusa_Chave_Numerica()
    {
        var perfil = new BaseProfile { Settings = { ["12"] = 1.0 } };

        var r = BaseProfileExchange.Preparar(perfil, Editaveis(BaseSettingId.MotionRange));

        Assert.Empty(r.Aplicar);
        Assert.Equal(new[] { "12" }, r.Desconhecidos);
    }

    // A ida-e-volta pelo ENVELOPE de arquivo mora em DriveLab.Studio.Tests: o ProfileExchange vive na
    // camada do Studio, e este projeto de testes so referencia o Core.
}
