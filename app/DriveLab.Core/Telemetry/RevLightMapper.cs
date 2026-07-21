// ============================================================================
//  DriveLab
//  RevLightMapper.cs — Converte telemetria de jogo na barra de rev-lights (verde→amarelo→vermelho→flash) e bandeiras.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Core.Telemetry;

/// <summary>
/// Função pura RPM→cores da barra. Sem estado nem tempo real: recebe o quadro de telemetria, as configurações
/// e um instante (segundos) para o piscar do shift, e devolve <c>BarLedCount</c> cores. Tudo determinístico,
/// então é 100% testável no host sem placa nem jogo.
/// </summary>
public static class RevLightMapper
{
    private static readonly WheelLedColor Off = new(0, 0, 0);

    /// <summary>Calcula a barra de rev-lights para um quadro de telemetria.</summary>
    /// <param name="t">Telemetria normalizada.</param>
    /// <param name="s">Configuração de zonas/cores/shift.</param>
    /// <param name="timeSeconds">Instante para o piscar do shift (ex.: relógio monotônico do app).</param>
    public static WheelLedColor[] Compute(GameTelemetry t, RevLightSettings s, double timeSeconds)
    {
        int n = Math.Max(1, s.BarLedCount);
        var bar = new WheelLedColor[n];

        // Sem dados (menu/replay parado) → barra apagada.
        if (!t.HasData)
        {
            Array.Fill(bar, Off);
            return bar;
        }

        // Bandeira tem prioridade: pinta a barra inteira com a cor da bandeira.
        if (TryFlagColor(t.Flag, s, out var flagColor))
        {
            Array.Fill(bar, flagColor);
            return bar;
        }

        float shiftRpm = t.ShiftRpm > 0 ? t.ShiftRpm : t.MaxRpm * s.ShiftFraction;
        if (shiftRpm <= 0)
        {
            Array.Fill(bar, Off);
            return bar;
        }

        // No/acima do shift → pisca a barra inteira na cor de shift.
        if (t.Rpm >= shiftRpm)
        {
            bool on = ((long)(timeSeconds * s.BlinkHz)) % 2 == 0;
            Array.Fill(bar, on ? s.ShiftColor : Off);
            return bar;
        }

        float startRpm = shiftRpm * s.StartFraction;
        float span = shiftRpm - startRpm;
        float frac = span > 0 ? (t.Rpm - startRpm) / span : 0f;
        frac = Math.Clamp(frac, 0f, 1f);

        int lit = (int)Math.Floor(frac * n);
        for (int i = 0; i < n; i++)
            bar[i] = i < lit ? ZoneColor((i + 0.5f) / n, s) : Off;

        return bar;
    }

    private static WheelLedColor ZoneColor(float pos, RevLightSettings s) =>
        pos <= s.GreenUpTo ? s.GreenColor :
        pos <= s.YellowUpTo ? s.YellowColor :
        s.RedColor;

    private static bool TryFlagColor(GameFlag flag, RevLightSettings s, out WheelLedColor color)
    {
        switch (flag)
        {
            case GameFlag.Yellow: color = s.FlagYellow; return true;
            case GameFlag.Blue: color = s.FlagBlue; return true;
            case GameFlag.White: color = s.FlagWhite; return true;
            case GameFlag.Red: color = s.FlagRed; return true;
            default: color = Off; return false;   // None/Green/Checkered/Black → comportamento normal de RPM
        }
    }
}
