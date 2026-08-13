// ============================================================================
//  DriveLab
//  FfbPlayback.cs — Onde a volta gravada está, e que força sai, a cada instante.
//
//  Lógica PURA: recebe "estamos no segundo X" e devolve a força. Quem move o
//  relógio e quem manda para a base ficam de fora — é o que permite testar o
//  ajuste de sincronia e os limites sem volante nenhum.
//
//  ⚠️ REPRODUÇÃO É FORÇA SEM PILOTO NO LAÇO. A volta foi gravada com alguém
//  segurando o volante e reagindo; na reprodução talvez não haja ninguém. Por
//  isso existe um TETO próprio, que começa baixo — ver TetoPct.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Capture;

public sealed class FfbPlayback
{
    private readonly FfbCapture _captura;

    public FfbPlayback(FfbCapture captura) => _captura = captura;

    public FfbCapture Captura => _captura;

    /// <summary>Quanto da força gravada sai, em %.
    ///
    /// <para><b>Nasce em 30 de propósito.</b> A volta original tinha um piloto segurando o volante e
    /// reagindo a cada força; na reprodução pode não haver ninguém, e a mesma força que era "carga
    /// de curva" contra as mãos vira o volante girando sozinho. Quem quiser a volta cheia sobe
    /// sabendo o que está fazendo.</para></summary>
    public double TetoPct { get; set; } = 30;

    /// <summary>Deslocamento manual, em segundos, para casar com o vídeo.
    ///
    /// <para>Positivo adianta a força (ela acontece antes), negativo atrasa. Existe porque o vídeo
    /// e a reprodução são dois relógios independentes: a contagem regressiva alinha o começo, mas
    /// depende do reflexo de quem aperta o play. O erro é CONSTANTE — acertou uma vez, vale a volta
    /// inteira —, e é por isso que um ajuste fino resolve em vez de precisar recomeçar.</para></summary>
    public double AjusteS { get; set; }

    public double DuracaoS => _captura.DurationS;

    /// <summary>A força a aplicar no instante `t` da reprodução, −1..1.
    ///
    /// <para>Interpola entre quadros: a base pode pedir força numa cadência diferente da gravação, e
    /// pegar "o quadro mais próximo" produziria degraus audíveis quando as duas taxas não são
    /// múltiplas.</para></summary>
    public double ForcaEm(double t)
    {
        var frames = _captura.Frames;
        if (frames.Count == 0) return 0;

        var pos = (t + AjusteS) * _captura.Header.RateHz;
        if (pos <= 0) return 0;                       // antes do começo: silêncio, não o primeiro quadro
        if (pos >= frames.Count - 1) return 0;        // depois do fim: silêncio

        var i = (int)pos;
        var f = pos - i;
        var bruta = frames[i].Force + (frames[i + 1].Force - frames[i].Force) * f;

        return Math.Clamp(bruta, -1, 1) * (Math.Clamp(TetoPct, 0, 100) / 100.0);
    }

    /// <summary>Onde o volante ESTAVA neste instante da gravação, em graus.
    ///
    /// <para>Não é usado para aplicar força — serve para a tela mostrar o volante da volta original
    /// ao lado do atual, e para quem quiser conferir se o replay está batendo com o que foi gravado.</para></summary>
    public double AnguloGravadoEm(double t)
    {
        var frames = _captura.Frames;
        if (frames.Count == 0) return 0;
        var pos = (t + AjusteS) * _captura.Header.RateHz;
        if (pos <= 0) return frames[0].AngleDeg;
        if (pos >= frames.Count - 1) return frames[^1].AngleDeg;
        var i = (int)pos;
        var f = pos - i;
        return frames[i].AngleDeg + (frames[i + 1].AngleDeg - frames[i].AngleDeg) * f;
    }

    /// <summary>Em que segundo do VÍDEO a reprodução está, para quem quiser conferir o alinhamento
    /// olhando o contador do player.</summary>
    public double SegundoDoVideoEm(double t) => _captura.Header.VideoStartS + t;
}
