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

    /// Amostras a partir do comando REAL do teste, e nao de uma copia da rampa escrita a mao aqui.
    /// A copia passaria a mentir no dia em que o comando mudasse — foi o que aconteceu quando a
    /// Rampa passou a alternar de sentido.
    private static List<ForceTestSample> AmostrarRampa(RampTest teste, Func<double, double> corrente)
    {
        var amostras = new List<ForceTestSample>();
        for (var i = 0; i < 160; i++)
        {
            var t = i * 0.05;
            var pedido = teste.ForcaEm(t).Constant;
            var a = corrente(pedido);
            amostras.Add(Amostra(t, pedido, corrente: a, torque: a * 0.39));
        }
        return amostras;
    }

    [Fact]
    public void Rampa_Passa_Quando_A_Corrente_Acompanha_A_Forca()
    {
        var teste = new RampTest();
        // Corrente proporcional ao pedido: 25 A no talo. E o comportamento saudavel.
        var r = teste.Avaliar(AmostrarRampa(teste, pedido => pedido * 25));

        Assert.True(r.Ok);
        Assert.Contains("9,75", r.Resumo.Replace(".", ","));
    }

    [Fact]
    public void Rampa_Acusa_Quando_A_Corrente_Satura_Antes_Da_Forca()
    {
        // O caso que custou caro a este projeto: a configuracao pede 15 Nm, mas o teto real e
        // corrente x Kt = 9,75. A partir de meia forca a corrente para de crescer.
        var teste = new RampTest();
        var r = teste.Avaliar(AmostrarRampa(teste,
            pedido => Math.Sign(pedido) * Math.Min(Math.Abs(pedido) * 25, 13)));   // satura em 13 A

        Assert.False(r.Ok);
        Assert.Contains("satura", r.Resumo);
        Assert.Contains(r.Detalhes, d => d.Contains("parou de acompanhar"));
    }

    [Fact]
    public void Rampa_Alterna_O_Sentido_Para_O_Volante_Nao_Acumular_Deslocamento()
    {
        // Empurrar sempre para o mesmo lado com forca crescente levava o volante ao fim do curso e
        // alem dele — a guarda de curso excedido desarmava o motor, corretamente. Medido na bancada
        // em 14/08/2026: 495 graus, exatos 45 alem do curso de 450. O teste tem que ir e voltar.
        var teste = new RampTest();
        var comandos = Enumerable.Range(0, 160).Select(i => teste.ForcaEm(i * 0.05).Constant).ToList();

        Assert.Contains(comandos, c => c > 0.9);    // chega ao talo dos dois lados
        Assert.Contains(comandos, c => c < -0.9);

        // O que de fato protege o curso: a INTEGRAL do comando volta a zero. Uma senoide com
        // amplitude crescente pode ir ao talo dos dois lados e ainda assim empurrar mais para um.
        var deslocamento = comandos.Sum() * 0.05;
        Assert.True(Math.Abs(deslocamento) < 0.5,
                    $"O comando empurra liquido para um lado ({deslocamento:0.00}) e o volante acumula curso");
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
    // ── Regeneração (resistor de freio) ──────────────────────────────────────────────────────

    /// <summary>Amostra com os contadores do chopper, que são ACUMULADOS desde o boot da base.</summary>
    private static ForceTestSample AmostraRegen(double t, uint acionamentos, double energiaJ,
                                                double torque = 5.0) =>
        new(t, 0.7, 0, torque, 12, 40, 0, acionamentos, energiaJ);

    [Fact]
    public void Regen_Passa_Quando_O_Resistor_Entra_E_Dissipa()
    {
        var teste = new RegenTest();
        // Contadores começam em valores altos de propósito: são acumulados desde o boot, e o que
        // vale é a DIFERENÇA. Um veredito que olhasse o valor absoluto passaria sempre.
        var amostras = new List<ForceTestSample>
        {
            AmostraRegen(0.0, 40_000, 120.0),
            AmostraRegen(8.0, 40_140, 123.4),
        };

        var r = teste.Avaliar(amostras);

        Assert.True(r.Ok);
        Assert.Contains("140", r.Resumo);          // 40140 - 40000
        Assert.Contains("3,4", r.Resumo.Replace(".", ","));   // 123,4 - 120,0
    }

    /// <summary>O caso que motivou o teste existir: a banda morta de regeneração alta demais
    /// silencia o resistor, e "frio e zerado" passaria por normal.</summary>
    [Fact]
    public void Regen_Acusa_Quando_O_Resistor_Nao_Entra()
    {
        var teste = new RegenTest();
        var amostras = new List<ForceTestSample>
        {
            AmostraRegen(0.0, 500, 12.0),
            AmostraRegen(8.0, 500, 12.0),   // nada mudou: não entrou nenhuma vez
        };

        var r = teste.Avaliar(amostras);

        Assert.False(r.Ok);
        Assert.Contains("não entrou", r.Resumo);
    }

    /// <summary>O defeito oposto, que a base de fato teve: 674 mil acionamentos com 27 J — o chopper
    /// picando no limiar em vez de dissipar. Muita ação com energia nenhuma NÃO é saúde.</summary>
    [Fact]
    public void Regen_Acusa_Quando_O_Resistor_Pica_Sem_Dissipar()
    {
        var teste = new RegenTest();
        var amostras = new List<ForceTestSample>
        {
            AmostraRegen(0.0, 0, 0.0),
            AmostraRegen(8.0, 9_000, 0.01),   // 9 mil acionamentos, 10 mJ
        };

        var r = teste.Avaliar(amostras);

        Assert.False(r.Ok);
        Assert.Contains("picando", r.Resumo);
    }

    [Fact]
    public void Regen_Pede_Para_Desacoplar_O_Aro()
    {
        // O preparo não é decoração: o teste sacode o eixo de lado a lado, e quem clica em Rodar
        // sem ler leva o rig junto. Se a chave sumir, a tela deixa de avisar em silêncio.
        Assert.NotEmpty(new RegenTest().PreparoKey);
    }
    // ── Curso excedido (a guarda que desarma) ────────────────────────────────────────────────

    private static ForceTestSample AmostraCurso(double t, double angulo, bool guardaAgiu) =>
        new(t, 0.30, angulo, 2.0, 6, 40, 0, 0, 0, guardaAgiu);

    [Fact]
    public void Curso_Passa_Quando_A_Guarda_Age()
    {
        var teste = new OvertravelTest();
        var amostras = new List<ForceTestSample>
        {
            AmostraCurso(0.0, 100, false),
            AmostraCurso(6.0, 440, false),
            AmostraCurso(9.0, 487, true),    // passou do curso e a guarda agiu
        };

        var r = teste.Avaliar(amostras);

        Assert.True(r.Ok);
        Assert.Contains("guarda agiu", r.Resumo);
    }

    /// <summary>O caso que o teste existe para pegar: o volante passou do fim do curso e a proteção
    /// ficou quieta.</summary>
    [Fact]
    public void Curso_Acusa_Quando_A_Guarda_Nao_Age()
    {
        var teste = new OvertravelTest();
        var amostras = new List<ForceTestSample>
        {
            AmostraCurso(0.0, 100, false),
            AmostraCurso(11.0, 500, false),
        };

        var r = teste.Avaliar(amostras);

        Assert.False(r.Ok);
        Assert.Contains("não agiu", r.Resumo);
    }

    /// <summary>Volante que mal se mexeu NÃO é falha da guarda — o teste sequer chegou a exercitá-la.
    /// Chamar isso de "guarda não agiu" mandaria a pessoa investigar uma proteção que está boa.</summary>
    [Fact]
    public void Curso_Distingue_Guarda_Ruim_De_Teste_Que_Nao_Rodou()
    {
        var teste = new OvertravelTest();
        var amostras = new List<ForceTestSample>
        {
            AmostraCurso(0.0, 5, false),
            AmostraCurso(11.0, 12, false),   // não saiu do lugar
        };

        var r = teste.Avaliar(amostras);

        Assert.False(r.Ok);
        Assert.Contains("não chegou a rodar", r.Resumo);
    }

    [Fact]
    public void Curso_Avisa_Que_Termina_Desarmado()
    {
        // O preparo carrega o aviso de que a base fica desarmada ao fim. Sem ele, o resultado
        // esperado do teste parece defeito.
        Assert.NotEmpty(new OvertravelTest().PreparoKey);
    }
}
