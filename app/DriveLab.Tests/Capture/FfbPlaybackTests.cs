// ============================================================================
//  DriveLab
//  FfbPlaybackTests.cs — Testes da reproducao de uma volta gravada.
//
//  O foco e SEGURANCA e SINCRONIA. Seguranca porque reproduzir e forca sem
//  piloto no laco: a volta foi gravada com alguem segurando o volante e
//  reagindo, e na reproducao pode nao haver ninguem. Sincronia porque o valor da
//  funcionalidade inteira depende de a zebra que se VE bater com a que se SENTE.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using DriveLab.Core.Capture;
using Xunit;

namespace DriveLab.Tests.Capture;

public class FfbPlaybackTests
{
    /// <summary>Uma volta de 1 s a 1000 Hz: forca 1,0 do meio em diante, zero antes.</summary>
    private static FfbPlayback Meia()
    {
        var frames = Enumerable.Range(0, 1000)
            .Select(i => new FfbFrame(i < 500 ? 0.0 : 1.0, i * 0.1, 0))
            .ToList();
        return new FfbPlayback(new FfbCapture(
            new FfbCaptureHeader { RateHz = 1000, VideoStartS = 12.5 }, frames)) { TetoPct = 100 };
    }

    // ── Seguranca ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_Teto_Nasce_Em_30_Porcento()
    {
        // E o valor que vale na PRIMEIRA reproducao, quando quem carregou o arquivo ainda nao sabe
        // o que aquela volta faz. A mesma forca que era "carga de curva" contra as maos de quem
        // gravou vira o volante girando sozinho se nao houver ninguem.
        var p = new FfbPlayback(new FfbCapture(new FfbCaptureHeader { RateHz = 1000 },
                                              new[] { new FfbFrame(1, 0, 0) }));
        Assert.Equal(30, p.TetoPct);
    }

    [Fact]
    public void O_Teto_Escala_A_Forca()
    {
        var p = Meia();
        p.TetoPct = 50;
        Assert.Equal(0.5, p.ForcaEm(0.75), 3);
    }

    [Fact]
    public void Antes_Do_Comeco_E_Depois_Do_Fim_A_Forca_E_ZERO()
    {
        // Nao "o primeiro quadro" nem "o ultimo": segurar a forca do fim depois que a volta acabou
        // deixaria o volante puxando para sempre, e ninguem estaria esperando por isso.
        var p = Meia();
        Assert.Equal(0, p.ForcaEm(-1.0));
        Assert.Equal(0, p.ForcaEm(5.0));
    }

    [Fact]
    public void Gravacao_Vazia_Nao_Aplica_Nada()
    {
        var p = new FfbPlayback(new FfbCapture(new FfbCaptureHeader { RateHz = 1000 },
                                               Array.Empty<FfbFrame>()));
        Assert.Equal(0, p.ForcaEm(0.5));
    }

    [Fact]
    public void Forca_Gravada_Fora_Da_Faixa_E_Limitada()
    {
        var p = new FfbPlayback(new FfbCapture(new FfbCaptureHeader { RateHz = 1000 },
                                               new[] { new FfbFrame(5, 0, 0), new FfbFrame(5, 0, 0), new FfbFrame(5, 0, 0) }))
                { TetoPct = 100 };
        Assert.Equal(1.0, p.ForcaEm(0.001), 3);
    }

    // ── Sincronia ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void O_Ajuste_Desloca_A_Volta_No_Tempo()
    {
        // O erro entre video e reproducao e CONSTANTE — vem do reflexo de quem aperta o play. Por
        // isso um deslocamento fixo resolve a volta inteira, em vez de precisar recomecar.
        var p = Meia();
        Assert.Equal(0, p.ForcaEm(0.4), 3);       // antes do degrau

        p.AjusteS = 0.2;                          // adianta a forca em 200 ms
        Assert.Equal(1.0, p.ForcaEm(0.4), 3);     // o degrau chega mais cedo
    }

    [Fact]
    public void O_Ajuste_Tambem_Atrasa()
    {
        var p = Meia();
        Assert.Equal(1.0, p.ForcaEm(0.6), 3);
        p.AjusteS = -0.2;
        Assert.Equal(0, p.ForcaEm(0.6), 3);
    }

    [Fact]
    public void Interpola_Entre_Quadros()
    {
        // A base pede forca numa cadencia que nao e multipla da gravacao. Pegar "o quadro mais
        // proximo" produziria degraus onde a gravacao e lisa.
        var frames = new[] { new FfbFrame(0, 0, 0), new FfbFrame(1, 0, 0), new FfbFrame(1, 0, 0) };
        var p = new FfbPlayback(new FfbCapture(new FfbCaptureHeader { RateHz = 100 }, frames)) { TetoPct = 100 };

        Assert.Equal(0.5, p.ForcaEm(0.005), 3);   // meio caminho entre o quadro 0 e o 1
    }

    [Fact]
    public void Diz_Em_Que_Segundo_Do_VIDEO_A_Volta_Esta()
    {
        // Permite conferir o alinhamento olhando o contador do player em vez de sentir e adivinhar.
        var p = Meia();
        Assert.Equal(12.5, p.SegundoDoVideoEm(0), 3);
        Assert.Equal(42.5, p.SegundoDoVideoEm(30), 3);
    }

    [Fact]
    public void O_Angulo_Gravado_Acompanha_A_Volta()
    {
        // Nao aplica forca: serve para a tela mostrar onde o volante ESTAVA, e para conferir se o
        // replay esta batendo com o que foi gravado.
        var p = Meia();
        Assert.Equal(0, p.AnguloGravadoEm(0), 1);
        Assert.Equal(50, p.AnguloGravadoEm(0.5), 1);
    }

    [Fact]
    public void A_Duracao_Vem_Da_Gravacao()
    {
        Assert.Equal(1.0, Meia().DuracaoS, 3);
    }
}
