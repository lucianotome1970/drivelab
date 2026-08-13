// ============================================================================
//  DriveLab
//  FfbCapture.cs — O arquivo de uma volta gravada: formato, leitura e escrita.
//
//  PARA QUE SERVE: gravar o force feedback de uma volta real e poder reproduzi-lo
//  depois, sem o jogo aberto. Dois usos, e o segundo é o mais forte:
//
//   1. DEMONSTRAÇÃO — quem montou a base sente uma volta de verdade sem ter o
//      jogo. Com o vídeo da mesma volta rodando junto, vê e sente ao mesmo tempo.
//   2. BANCADA DE AJUSTE — a MESMA volta, repetida quantas vezes quiser. Hoje,
//      para saber se mexer na curva melhorou, é preciso dirigir — e nenhuma volta
//      é igual à outra, então se compara sensação com memória. Com a gravação,
//      muda-se um ajuste e ouve-se a diferença com todo o resto igual.
//
//  ── DUAS DECISÕES QUE DEFINEM O FORMATO ───────────────────────────────────
//
//  A força é NORMALIZADA, nunca em Nm. Quem grava numa base de 15 Nm e quem
//  reproduz numa de 5 têm de ouvir a mesma volta, escalada — não cortada. É o
//  mesmo motivo pelo qual o jogo manda força normalizada e não torque: ele não
//  sabe que volante está do outro lado.
//
//  A taxa é FIXA e vive no cabeçalho, em vez de um instante por quadro. Um
//  timestamp por amostra custaria mais que a própria amostra, e a captura nasce
//  de um laço de período fixo — o tempo é o índice.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Text;
using System.Text.Json;

namespace DriveLab.Core.Capture;

/// <summary>Um instante da volta.</summary>
/// <param name="Force">Força pedida pelo jogo, −1..1 (fração do fundo de escala de QUEM GRAVOU).</param>
/// <param name="AngleDeg">Onde o volante estava, em graus a partir do centro.</param>
/// <param name="VelDegPerS">Velocidade do volante naquele instante, em graus por segundo.</param>
public readonly record struct FfbFrame(double Force, double AngleDeg, double VelDegPerS);

/// <summary>O que a gravação é: de onde veio, e o que precisa para tocar junto do vídeo.</summary>
public sealed record FfbCaptureHeader
{
    /// <summary>Quadros por segundo. Alto de propósito: a textura que o piloto sente (zebra, perda
    /// de aderência, vibração) vive entre 10 e 100 Hz, e amostrar abaixo de 250 Hz joga fora o que
    /// o jogo mandou — a 25 Hz sobraria só o "peso" do carro.</summary>
    public int RateHz { get; init; } = 1000;

    public string Game { get; init; } = "";
    public string Car { get; init; } = "";
    public string Track { get; init; } = "";
    public double LapTimeS { get; init; }

    /// <summary>Vídeo da MESMA volta, para assistir junto.</summary>
    public string VideoUrl { get; init; } = "";

    /// <summary>Em que segundo do vídeo a volta começa — é o que permite alinhar sem adivinhação.</summary>
    public double VideoStartS { get; init; }

    /// <summary>Fundo de escala de quem gravou, em Nm. NÃO é usado para reproduzir (a força é
    /// normalizada); serve para quem lê saber com que base a volta foi feita.</summary>
    public double FullScaleNm { get; init; }

    public string Firmware { get; init; } = "";
    public string RecordedAt { get; init; } = "";
    public string Notes { get; init; } = "";
}

/// <summary>Uma volta gravada: cabeçalho + quadros.</summary>
public sealed record FfbCapture(FfbCaptureHeader Header, IReadOnlyList<FfbFrame> Frames)
{
    public double DurationS => Header.RateHz > 0 ? (double)Frames.Count / Header.RateHz : 0;
}

/// <summary>Lê e escreve o arquivo de captura.
///
/// <para>Formato: a linha mágica <c>DLFFB1</c>, uma linha de JSON com o cabeçalho, e então os
/// quadros em binário — três inteiros de 16 bits por quadro, little-endian.</para>
///
/// <para>O cabeçalho é JSON de propósito: dá para abrir num editor e ver de que volta se trata sem
/// nenhuma ferramenta. Os quadros são binários porque são milhares por segundo, e em texto o
/// arquivo ficaria dez vezes maior sem ninguém nunca lê-los a olho.</para></summary>
public static class FfbCaptureFile
{
    public const string Magic = "DLFFB1";

    /// <summary>Escalas dos três campos. Fixas no formato, não no leitor: mudá-las quebraria
    /// arquivos já gravados, e por isso a mágica tem número de versão.</summary>
    private const double ForceScale = 10000.0;   // ±1,0000 em passos de 0,0001
    private const double AngleScale = 10.0;      // décimos de grau, como a telemetria
    private const double VelScale   = 4.0;       // ±8191°/s — muito além do que um volante faz

    public static void Write(Stream destino, FfbCapture captura)
    {
        using var w = new BinaryWriter(destino, Encoding.UTF8, leaveOpen: true);
        w.Write(Encoding.UTF8.GetBytes(Magic + "\n"));
        w.Write(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(captura.Header) + "\n"));

        foreach (var f in captura.Frames)
        {
            w.Write(Saturar(f.Force * ForceScale));
            w.Write(Saturar(f.AngleDeg * AngleScale));
            w.Write(Saturar(f.VelDegPerS * VelScale));
        }
    }

    public static FfbCapture Read(Stream origem)
    {
        using var leitor = new BinaryReader(origem, Encoding.UTF8, leaveOpen: true);

        if (LerLinha(leitor) != Magic)
            throw new InvalidDataException("Não é um arquivo de gravação do DriveLab.");

        var json = LerLinha(leitor);
        var header = JsonSerializer.Deserialize<FfbCaptureHeader>(json)
                     ?? throw new InvalidDataException("Cabeçalho da gravação ilegível.");
        if (header.RateHz <= 0)
            throw new InvalidDataException("Gravação sem taxa de amostragem — não dá para saber a duração.");

        var frames = new List<FfbFrame>();
        while (true)
        {
            // Um quadro truncado no fim (gravação interrompida, disco cheio) NÃO invalida o que veio
            // antes: perde-se o último quadro, não a volta inteira.
            var bloco = leitor.ReadBytes(6);
            if (bloco.Length < 6) break;
            frames.Add(new FfbFrame(
                BitConverter.ToInt16(bloco, 0) / ForceScale,
                BitConverter.ToInt16(bloco, 2) / AngleScale,
                BitConverter.ToInt16(bloco, 4) / VelScale));
        }

        return new FfbCapture(header, frames);
    }

    private static short Saturar(double v) =>
        (short)Math.Clamp(Math.Round(v), short.MinValue, short.MaxValue);

    private static string LerLinha(BinaryReader r)
    {
        var bytes = new List<byte>();
        while (true)
        {
            var b = r.ReadByte();
            if (b == (byte)'\n') break;
            bytes.Add(b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
