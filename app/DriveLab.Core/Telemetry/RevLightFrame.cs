// ============================================================================
//  DriveLab
//  RevLightFrame.cs — Junta as cores dos botões (0-9) com a barra de rev (10-17) num WheelLedReport de 18 LEDs.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Core.Telemetry;

/// <summary>
/// Monta o cordão completo de LEDs do aro: os <see cref="ButtonLedCount"/> primeiros pixels são as cores dos
/// botões (definidas pelo usuário), seguidos da barra de rev calculada pelo <see cref="RevLightMapper"/>.
/// Assim os rev-lights preenchem a barra (10-17) sem apagar as cores dos botões (0-9).
/// </summary>
public static class RevLightFrame
{
    /// <summary>Quantidade de LEDs de botão no aro DriveLab (pixels 0-9).</summary>
    public const int ButtonLedCount = 10;

    private static readonly WheelLedColor Off = new(0, 0, 0);

    /// <summary>Combina cores de botão + barra num único report. Botões faltando viram apagados; sobrando, truncados.</summary>
    public static WheelLedReport Build(IReadOnlyList<WheelLedColor> buttonColors, IReadOnlyList<WheelLedColor> bar, byte brightness)
    {
        var colors = new WheelLedColor[ButtonLedCount + bar.Count];
        for (int i = 0; i < ButtonLedCount; i++)
            colors[i] = i < buttonColors.Count ? buttonColors[i] : Off;
        for (int i = 0; i < bar.Count; i++)
            colors[ButtonLedCount + i] = bar[i];
        return new WheelLedReport(brightness, colors);
    }
}
