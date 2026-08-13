// ============================================================================
//  DriveLab
//  FfbCaptureFileTests.cs — Testes do arquivo de volta gravada.
//
//  O que se protege aqui e a INTEROPERABILIDADE no tempo: a gravacao e feita
//  para ser guardada e trocada entre pessoas. Um arquivo que deixa de abrir, ou
//  que abre dizendo outra coisa, perde uma volta que ninguem vai rodar de novo.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using System.Text;
using DriveLab.Core.Capture;
using Xunit;

namespace DriveLab.Tests.Capture;

public class FfbCaptureFileTests
{
    private static FfbCapture Exemplo(int quadros = 5) => new(
        new FfbCaptureHeader
        {
            RateHz = 1000, Game = "ACC", Car = "Porsche 992 GT3 R", Track = "Monza",
            LapTimeS = 107.4, VideoUrl = "https://youtu.be/abc123", VideoStartS = 12.5,
            FullScaleNm = 15, Firmware = "0.2.3", RecordedAt = "2026-08-13T10:00:00Z",
        },
        Enumerable.Range(0, quadros)
            .Select(i => new FfbFrame(i / 10.0 - 0.2, i * 3.0, i * 50.0))
            .ToList());

    private static FfbCapture IdaEVolta(FfbCapture c)
    {
        using var ms = new MemoryStream();
        FfbCaptureFile.Write(ms, c);
        ms.Position = 0;
        return FfbCaptureFile.Read(ms);
    }

    [Fact]
    public void O_Cabecalho_Sobrevive_A_Ida_E_Volta()
    {
        var lido = IdaEVolta(Exemplo()).Header;
        var original = Exemplo().Header;

        Assert.Equal(original.RateHz, lido.RateHz);
        Assert.Equal(original.Game, lido.Game);
        Assert.Equal(original.Track, lido.Track);
        Assert.Equal(original.VideoUrl, lido.VideoUrl);
        Assert.Equal(original.VideoStartS, lido.VideoStartS);
        Assert.Equal(original.FullScaleNm, lido.FullScaleNm);
    }

    [Fact]
    public void Os_Quadros_Sobrevivem_Com_Precisao_Util()
    {
        var original = Exemplo(20);
        var lido = IdaEVolta(original);

        Assert.Equal(original.Frames.Count, lido.Frames.Count);
        for (var i = 0; i < original.Frames.Count; i++)
        {
            Assert.Equal(original.Frames[i].Force, lido.Frames[i].Force, 3);
            Assert.Equal(original.Frames[i].AngleDeg, lido.Frames[i].AngleDeg, 1);
            Assert.Equal(original.Frames[i].VelDegPerS, lido.Frames[i].VelDegPerS, 0);
        }
    }

    [Fact]
    public void A_Forca_E_Guardada_NORMALIZADA()
    {
        // A decisao central do formato: quem gravou numa base de 15 Nm e quem reproduz numa de 5
        // tem de ouvir a MESMA volta, escalada — nao cortada. Se a forca fosse gravada em Nm, a
        // segunda base saturaria a volta inteira. Aqui 1,0 significa "o maximo daquela base".
        var c = new FfbCapture(new FfbCaptureHeader { RateHz = 1000, FullScaleNm = 15 },
                               new[] { new FfbFrame(1.0, 0, 0), new FfbFrame(-1.0, 0, 0) });

        var lido = IdaEVolta(c);

        Assert.Equal(1.0, lido.Frames[0].Force, 4);
        Assert.Equal(-1.0, lido.Frames[1].Force, 4);
        Assert.Equal(15, lido.Header.FullScaleNm);   // fica registrado, mas NAO escala nada
    }

    [Fact]
    public void Forca_Alem_Do_Maximo_Satura_Em_Vez_De_Dar_A_Volta()
    {
        // Numero que estoura o campo TEM de saturar. Se desse a volta, um pico de forca viraria um
        // pico do lado CONTRARIO — o volante bateria para o outro lado na hora exata da zebra.
        var c = new FfbCapture(new FfbCaptureHeader { RateHz = 1000 },
                               new[] { new FfbFrame(9.0, 0, 0), new FfbFrame(-9.0, 0, 0) });

        var lido = IdaEVolta(c);

        Assert.True(lido.Frames[0].Force > 0, "saturou para o lado certo");
        Assert.True(lido.Frames[1].Force < 0, "saturou para o lado certo");
    }

    [Fact]
    public void A_Duracao_Sai_Da_Taxa_E_Da_Quantidade()
    {
        var c = new FfbCapture(new FfbCaptureHeader { RateHz = 1000 },
                               Enumerable.Repeat(new FfbFrame(0, 0, 0), 2500).ToList());
        Assert.Equal(2.5, c.DurationS, 3);
    }

    // ── O que nao pode abrir ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Arquivo_De_Outro_Tipo_E_Recusado_Com_Clareza()
    {
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes("isto e um txt qualquer\n"));
        var erro = Assert.Throws<InvalidDataException>(() => FfbCaptureFile.Read(ms));
        Assert.Contains("DriveLab", erro.Message);
    }

    [Fact]
    public void Gravacao_Sem_Taxa_E_Recusada()
    {
        // Sem taxa nao ha como saber a duracao nem em que ritmo tocar — tocar no ritmo errado
        // entregaria uma volta acelerada ou arrastada, que e pior que nao tocar.
        using var ms = new MemoryStream();
        FfbCaptureFile.Write(ms, new FfbCapture(new FfbCaptureHeader { RateHz = 0 },
                                                new[] { new FfbFrame(0, 0, 0) }));
        ms.Position = 0;
        var erro = Assert.Throws<InvalidDataException>(() => FfbCaptureFile.Read(ms));
        Assert.Contains("taxa", erro.Message);
    }

    [Fact]
    public void Quadro_Truncado_No_Fim_Nao_Invalida_A_Volta_INTEIRA()
    {
        // Gravacao interrompida, disco cheio, cabo puxado: perde-se o ultimo quadro, nao a sessao.
        using var ms = new MemoryStream();
        FfbCaptureFile.Write(ms, Exemplo(10));
        var bytes = ms.ToArray();
        using var truncado = new MemoryStream(bytes[..^3]);   // meio quadro a menos

        var lido = FfbCaptureFile.Read(truncado);

        Assert.Equal(9, lido.Frames.Count);
        Assert.Equal("Monza", lido.Header.Track);
    }
}
