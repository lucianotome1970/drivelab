// ============================================================================
//  DriveLab
//  ForceTestTests.cs — Testes da REGUA DE AVALIACAO dos testes de forca.
//
//  O que se protege aqui nao e o motor: e a CONCLUSAO. Um veredito errado manda
//  a pessoa apertar parafuso que nao esta solto, ou trocar fonte que esta boa —
//  e ela nao tem como saber que o app errou. Por isso cada teste tem dois casos:
//  um conjunto de amostras que DEVE passar e um que DEVE acusar.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using DriveLab.Core.Testing;
using Xunit;

namespace DriveLab.Tests.Testing;

public class ForceTestTests
{
    private static ForceTestSample Amostra(double t, double pedido, double angulo = 0,
                                           double torque = 0, double corrente = 0, double clipping = 0) =>
        new(t, pedido, angulo, torque, corrente, 40, clipping);

    // ── Rampa ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rampa_Passa_Quando_A_Corrente_Acompanha_A_Forca()
    {
        var teste = new RampTest();
        // Corrente proporcional ao pedido: 25 A no talo. E o comportamento saudavel.
        var amostras = Enumerable.Range(0, 80)
            .Select(i => { var t = i * 0.1; var pedido = Math.Clamp(t / 6.0, 0, 1);
                           return Amostra(t, pedido, corrente: pedido * 25, torque: pedido * 9.75); })
            .ToList();

        var r = teste.Avaliar(amostras);

        Assert.True(r.Ok);
        Assert.Contains("9,75", r.Resumo.Replace(".", ","));
    }

    [Fact]
    public void Rampa_Acusa_Quando_A_Corrente_Satura_Antes_Da_Forca()
    {
        // O caso que custou caro a este projeto: a configuracao pede 15 Nm, mas o teto real e
        // corrente x Kt = 9,75. A partir de meia forca a corrente para de crescer.
        var teste = new RampTest();
        var amostras = Enumerable.Range(0, 80)
            .Select(i => { var t = i * 0.1; var pedido = Math.Clamp(t / 6.0, 0, 1);
                           var corrente = Math.Min(pedido * 25, 13);   // satura em 13 A
                           return Amostra(t, pedido, corrente: corrente, torque: corrente * 0.39); })
            .ToList();

        var r = teste.Avaliar(amostras);

        Assert.False(r.Ok);
        Assert.Contains("satura", r.Resumo);
        Assert.Contains(r.Detalhes, d => d.Contains("parou de acompanhar"));
    }

    // ── Impacto ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Impacto_Acusa_Quando_Metade_Do_Pico_E_Cortada()
    {
        var teste = new ImpactTest();
        var amostras = new List<ForceTestSample>
        {
            Amostra(1.0, 1, torque: 9.7, clipping: 68),
            Amostra(2.0, -1, torque: -9.7, clipping: 71),
        };

        var r = teste.Avaliar(amostras);

        Assert.False(r.Ok);
        Assert.Contains("cortado", r.Resumo);
    }

    [Fact]
    public void Impacto_Bate_Nos_Dois_Sentidos()
    {
        // Bater sempre para o mesmo lado empurraria o volante contra o batente, e o teste mediria o
        // batente em vez do impacto.
        var teste = new ImpactTest();
        var lados = new[] { 1.05, 2.05, 3.05 }.Select(t => Math.Sign(teste.ForcaEm(t).Constant)).ToList();

        Assert.Contains(1, lados);
        Assert.Contains(-1, lados);
        Assert.Equal(0, teste.ForcaEm(1.5).Constant);   // entre os pulsos, silencio
    }

    // ── Vibracao ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Vibracao_Varre_A_Faixa_Inteira()
    {
        var teste = new VibrationTest();
        Assert.Equal(VibrationTest.HzInicial, teste.HzEm(0), 3);
        Assert.Equal(VibrationTest.HzFinal, teste.HzEm(teste.DuracaoS), 3);
    }

    [Fact]
    public void Vibracao_Nao_Da_Salto_De_Fase_Na_Varredura()
    {
        // A fase e INTEGRADA. Com sin(2π·f(t)·t) a fase salta a cada passo e a varredura vira uma
        // sequencia de degraus — testaria o degrau, nao a frequencia. Aqui o sinal tem de ser
        // continuo: dois instantes vizinhos nao podem dar valores distantes.
        var teste = new VibrationTest();
        var anterior = teste.ForcaEm(0).Constant;
        for (var t = 0.001; t < teste.DuracaoS; t += 0.001)
        {
            var atual = teste.ForcaEm(t).Constant;
            Assert.True(Math.Abs(atual - anterior) < 0.06,
                        $"salto de {Math.Abs(atual - anterior):0.000} em t={t:0.000}s");
            anterior = atual;
        }
    }

    /// <summary>Resposta de uma inercia livre: com a mesma forca, o deslocamento vai com 1/f². E
    /// esta a curva que a analise tem de considerar NORMAL — nao uma linha plana.</summary>
    private static List<ForceTestSample> VarreduraFisica(VibrationTest teste, double fatorEm5a7Hz = 1.0)
    {
        var amostras = new List<ForceTestSample>();
        for (var i = 0; i < 120; i++)
        {
            var t  = i * 0.1;
            var hz = teste.HzEm(t);
            var amplitude = 2500.0 / (hz * hz);              // 100° a 5 Hz, 4° a 25 Hz
            if (t is > 5.0 and < 6.5) amplitude *= fatorEm5a7Hz;
            amostras.Add(Amostra(t, 0.3, angulo: (i % 2 == 0 ? amplitude : -amplitude)));
        }
        return amostras;
    }

    [Fact]
    public void Vibracao_Acusa_Ressonancia_Acima_DA_CURVA_Nao_Da_Media()
    {
        // Ressonancia de verdade e a janela que DESTOA da curva 1/f² — aqui, 6x acima do que a
        // fisica preve para aquela frequencia.
        var teste = new VibrationTest();

        var r = teste.Avaliar(VarreduraFisica(teste, fatorEm5a7Hz: 6.0));

        Assert.False(r.Ok);
        Assert.Contains("Ressonância", r.Resumo);
    }

    [Fact]
    public void Vibracao_Passa_Quando_A_Excursao_Cai_Com_A_Frequencia()
    {
        // CASO REAL da bancada (2026-08-12): 112° a 6 Hz contra 2,2° a 24 Hz. A analise antiga
        // comparava com a MEDIA das janelas, acusava "4,5x mais que a media" e mandava procurar
        // folga no suporte — um defeito que nao existia. A excursao cair com a frequencia e o
        // comportamento esperado de uma inercia, nao sintoma.
        var teste = new VibrationTest();

        var r = teste.Avaliar(VarreduraFisica(teste));

        Assert.True(r.Ok);
        Assert.Contains(r.Detalhes, d => d.Contains("1/f²"));
    }

    // ── Mola ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Mola_Acusa_Encoder_Invertido()
    {
        // O volante se AFASTA do centro: a base empurra para onde deveria puxar. E o jeito mais
        // rapido de achar sentido de encoder invertido sem entrar num jogo.
        var teste = new SpringTest();
        var amostras = new List<ForceTestSample>
        {
            Amostra(0.0, 0, angulo: 30), Amostra(2.0, 0, angulo: 45),
            Amostra(4.0, 0, angulo: 60), Amostra(6.0, 0, angulo: 80),
        };

        var r = teste.Avaliar(amostras);

        Assert.False(r.Ok);
        Assert.Contains(r.Detalhes, d => d.Contains("invertido"));
    }

    [Fact]
    public void Mola_Reprova_Quando_O_Volante_Nao_Se_Move()
    {
        // Caso REAL de 2026-08-12: o firmware descartava os efeitos do report 0x10, nenhuma forca
        // saia, e o teste aprovava dizendo "comecou a 5,1°, terminou a 5,1°" — relatando com
        // precisao que nada acontecera, e chamando isso de sucesso.
        var teste = new SpringTest();
        var amostras = new List<ForceTestSample>
        {
            Amostra(0.0, 0, angulo: 5.1), Amostra(2.0, 0, angulo: 5.1),
            Amostra(4.0, 0, angulo: 5.1), Amostra(6.0, 0, angulo: 5.1),
        };

        var r = teste.Avaliar(amostras);

        Assert.False(r.Ok);
        Assert.Contains("não se moveu", r.Resumo);
    }

    [Fact]
    public void Mola_Passa_Quando_Volta_Ao_Centro()
    {
        var teste = new SpringTest();
        var amostras = new List<ForceTestSample>
        {
            Amostra(0.0, 0, angulo: 60), Amostra(2.0, 0, angulo: 25),
            Amostra(4.0, 0, angulo: 8), Amostra(6.0, 0, angulo: 3),
        };

        Assert.True(teste.Avaliar(amostras).Ok);
    }

    [Fact]
    public void Mola_Nao_Conclui_Nada_Se_Comecou_No_Centro()
    {
        // Sem deslocamento a mola nao teve o que fazer — dizer "passou" seria mentira por omissao.
        var teste = new SpringTest();
        var amostras = new List<ForceTestSample>
        {
            Amostra(0.0, 0, angulo: 1), Amostra(2.0, 0, angulo: 1),
            Amostra(4.0, 0, angulo: 0), Amostra(6.0, 0, angulo: 1),
        };

        var r = teste.Avaliar(amostras);

        Assert.True(r.Ok);
        Assert.Contains("gire-o e repita", r.Resumo);
    }

    // ── Garantias comuns ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Nenhum_Teste_Pede_Mais_Do_Que_O_Maximo()
    {
        // Forca fora de -1..1 seria cortada em silencio pelo transporte, e o teste estaria medindo
        // outra coisa que nao o que diz medir.
        foreach (var teste in ForceTests.Todos)
            for (var t = 0.0; t <= teste.DuracaoS; t += 0.01)
            {
                var f = teste.ForcaEm(t);
                Assert.InRange(f.Constant, -1, 1);
                Assert.InRange(f.Spring, -1, 1);
                Assert.InRange(f.Periodic, -1, 1);
                Assert.InRange(f.Damper, -1, 1);
            }
    }

    [Fact]
    public void O_Pico_Declarado_Bate_Com_O_Que_O_Teste_Pede()
    {
        // A tela avisa a pessoa pelo PicoDeForca antes de aplicar. Se o declarado for menor que o
        // real, o aviso mente — e o aviso existe justamente porque isso mexe um volante de verdade.
        foreach (var teste in ForceTests.Todos)
        {
            var maior = 0.0;
            for (var t = 0.0; t <= teste.DuracaoS; t += 0.01)
            {
                var f = teste.ForcaEm(t);
                maior = Math.Max(maior, Math.Abs(f.Constant));
                maior = Math.Max(maior, Math.Abs(f.Spring));
            }
            Assert.True(maior <= teste.PicoDeForca + 0.001,
                        $"{teste.Id}: declara pico {teste.PicoDeForca} e pede {maior:0.00}");
        }
    }

    [Fact]
    public void Todo_Teste_Sem_Amostras_Diz_Que_Nao_Sabe()
    {
        // Sem dado, o veredito honesto e "nao sei", nunca "passou".
        foreach (var teste in ForceTests.Todos)
        {
            var r = teste.Avaliar(Array.Empty<ForceTestSample>());
            Assert.False(r.Ok);
        }
    }
}
