// ============================================================================
//  DriveLab
//  EncoderHealthTests.cs — a régua que traduz erro de encoder em perda de força.
//
//  O QUE ESTES TESTES PROTEGEM: esta é uma ferramenta de DIAGNÓSTICO, e um
//  diagnóstico errado é pior que nenhum — manda a pessoa desmontar o que estava
//  bom, ou a tranquiliza sobre o que está ruim. Os casos abaixo são os quatro
//  vereditos possíveis, mais os dois em que a resposta correta é "não sei".
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Diagnostics;

namespace DriveLab.Tests.Testing;

public class EncoderHealthTests
{
    private static EncoderMeasurement Medicao(double exc, double res, double fase = 180, int pp = 15) =>
        new(Valido: true, CoberturaVolta: 1.0, ExcentricidadeGraus: exc, ResiduoGraus: res,
            FaseGraus: fase, PolePairs: pp);

    private static string Tudo(IEnumerable<string> linhas) => string.Join("\n", linhas);

    // ── A CONTA ──────────────────────────────────────────────────────────────

    [Fact]
    public void Encoder_perfeito_entrega_torque_inteiro()
    {
        var i = EncoderHealth.Impacto(0, 15);
        Assert.Equal(100, i.TorqueEntreguePct, 3);
        Assert.Equal(0, i.CalorExtraPct, 3);
    }

    /// <summary>O multiplicador dos pares de polos é a razão de a ferramenta existir: 1,7° soa
    /// desprezível e não é. Se esta conta sumir, a tela volta a mostrar um número inofensivo.</summary>
    [Fact]
    public void Erro_mecanico_vira_erro_eletrico_multiplicado_pelos_pares_de_polos()
    {
        Assert.Equal(25.5, EncoderHealth.Impacto(1.7, 15).ErroEletricoGraus, 3);
        Assert.Equal(51.0, EncoderHealth.Impacto(1.7, 30).ErroEletricoGraus, 3);
    }

    [Fact]
    public void Torque_cai_com_o_cosseno_do_erro_eletrico()
    {
        // 60° elétricos = cos 60° = exatamente metade do torque. Caso fechado, sem arredondamento.
        var i = EncoderHealth.Impacto(4.0, 15);   // 4 × 15 = 60° elétricos
        Assert.Equal(50, i.TorqueEntreguePct, 3);
        Assert.Equal(50, i.PerdaTorquePct, 3);
    }

    /// <summary>O calor vai com o QUADRADO da corrente. Com metade do torque por ampère, a corrente
    /// dobra e o aquecimento quadruplica — +300%. É o número que explica motor quente sem erro.</summary>
    [Fact]
    public void Calor_sobe_com_o_quadrado_da_corrente_extra()
    {
        var i = EncoderHealth.Impacto(4.0, 15);   // 60° elétricos, cos = 0,5
        Assert.Equal(100, i.CorrenteExtraPct, 3);
        Assert.Equal(300, i.CalorExtraPct, 3);
    }

    /// <summary>Passados 90° elétricos a corrente empurra o rotor para TRÁS. O cosseno fica negativo
    /// e um "torque de −30%" na tela não comunica nada; travamos em zero e o texto assume.</summary>
    [Fact]
    public void Alem_de_noventa_graus_eletricos_nao_ha_torque_util()
    {
        var i = EncoderHealth.Impacto(8.0, 15);   // 120° elétricos
        Assert.Equal(0, i.TorqueEntreguePct);
        Assert.Equal(0, i.CorrenteExtraPct);      // não existe corrente que entregue: não inventamos
        var r = EncoderHealth.Avaliar(Medicao(8.0, 0));
        Assert.False(r.Ok);
        Assert.Contains("para trás", Tudo(r.Detalhes));
    }

    // ── OS VEREDITOS ─────────────────────────────────────────────────────────

    [Fact]
    public void Encoder_bem_montado_passa_e_manda_procurar_em_outro_lugar()
    {
        var r = EncoderHealth.Avaliar(Medicao(0.10, 0.05));
        Assert.True(r.Ok);
        Assert.Contains("procure em outro lugar", Tudo(r.Detalhes));
    }

    [Fact]
    public void Excentricidade_dominante_culpa_o_ima_e_diz_para_que_lado_empurrar()
    {
        var r = EncoderHealth.Avaliar(Medicao(exc: 1.70, res: 0.20, fase: 212));
        Assert.False(r.Ok);
        var texto = Tudo(r.Detalhes);
        Assert.Contains("FORA DE CENTRO", texto);
        Assert.Contains("212", texto);            // sem a fase, a pessoa não sabe onde mexer
    }

    /// <summary>⚠️ O TESTE QUE PEGA O ERRO QUE JÁ COMETEMOS. A primeira versão comparava a amplitude
    /// com um limiar e dizia "ímã bem centrado" com o resíduo cinco vezes maior. Se a senoide não
    /// descreve o erro, a amplitude dela é o ajuste de um modelo que não serve — e culpar o ímã por
    /// ela é dar número a um palpite.</summary>
    [Fact]
    public void Residuo_dominante_NAO_culpa_o_ima()
    {
        var r = EncoderHealth.Avaliar(Medicao(exc: 0.40, res: 2.00));
        var texto = Tudo(r.Detalhes);
        Assert.Contains("NÃO tem cara de ímã descentrado", texto);
        Assert.DoesNotContain("FORA DE CENTRO", texto);
    }

    /// <summary>Erro pequeno passa MESMO com o resíduo dominante, e é o comportamento certo: 0,17° de
    /// excentricidade com 0,86° de resíduo custam 3,6% de força, que ninguém sente. A forma do erro
    /// só interessa quando ele cobra alguma coisa — perseguir a origem de um erro irrelevante manda
    /// a pessoa desmontar o que está bom.
    ///
    /// <para>Estes números são justamente os que a primeira versão da ferramenta reportou, na época
    /// em que ela media com cobertura insuficiente. Ficam aqui para não voltarem a ser lidos como
    /// defeito: naquele dia eles não eram medição nenhuma, e mesmo se fossem não seriam problema.</para></summary>
    [Fact]
    public void Erro_pequeno_passa_mesmo_com_residuo_dominante()
    {
        var r = EncoderHealth.Avaliar(Medicao(exc: 0.17, res: 0.86));
        Assert.True(r.Ok);
        Assert.Contains("procure em outro lugar", Tudo(r.Detalhes));
    }

    [Fact]
    public void Duas_causas_somadas_mandam_resolver_a_mais_facil_primeiro()
    {
        var r = EncoderHealth.Avaliar(Medicao(exc: 1.70, res: 1.62));
        Assert.Contains("duas coisas somadas", Tudo(r.Detalhes));
    }

    /// <summary>O pior ponto SOMA as duas causas: em algum lugar da volta a senoide se alinha com o
    /// resto. É esse ponto que produz o tremor, e é por ele que o veredito julga — julgar pela média
    /// diria "quase tudo bem" sobre um volante que treme.</summary>
    [Fact]
    public void Veredito_usa_o_pior_ponto_da_volta_e_nao_a_media()
    {
        var m = Medicao(exc: 1.70, res: 1.62);
        Assert.Equal(3.32, EncoderHealth.PiorErroMecanico(m), 3);

        var r = EncoderHealth.Avaliar(m);
        Assert.False(r.Ok);
        // 3,32 × 15 = 49,8° elétricos → cos ≈ 0,645 → perde ~35%
        Assert.Contains("35%", r.Resumo);
    }

    // ── QUANDO A RESPOSTA CERTA É "NÃO SEI" ──────────────────────────────────

    /// <summary>Cobertura curta dá um número convincente e errado: a excentricidade tem período de
    /// uma volta, e ajustar a senoide a um quarto dela deixa qualquer amplitude encaixar mudando a
    /// fase. Calar é a resposta honesta — quem lê não tem como saber que aquilo não é medição.</summary>
    [Fact]
    public void Varredura_curta_nao_responde_e_explica_por_que()
    {
        var m = Medicao(1.70, 0.20) with { Valido = false, CoberturaVolta = 0.27 };
        var r = EncoderHealth.Avaliar(m);
        Assert.False(r.Ok);
        Assert.Contains("Não consegui medir", r.Resumo);
        Assert.Contains("0,75", Tudo(r.Detalhes).Replace("0.75", "0,75"));
        // e não pode vazar um veredito sobre o ímã a partir de dados que não o sustentam
        Assert.DoesNotContain("FORA DE CENTRO", Tudo(r.Detalhes));
    }

    [Fact]
    public void Cobertura_zero_sugere_motor_sem_energia()
    {
        var m = Medicao(0, 0) with { Valido = false, CoberturaVolta = 0 };
        Assert.Contains("energizado", Tudo(EncoderHealth.Avaliar(m).Detalhes));
    }

    [Fact]
    public void Sem_pares_de_polos_nao_ha_conversao_possivel()
    {
        var r = EncoderHealth.Avaliar(Medicao(1.7, 0.2, pp: 0));
        Assert.False(r.Ok);
        Assert.Contains("pares de polos", r.Resumo);
    }
}
