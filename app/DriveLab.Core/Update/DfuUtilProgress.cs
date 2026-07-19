// ============================================================================
//  DriveLab
//  DfuUtilProgress.cs — Parser puro de linhas de progresso do dfu-util
//  ("Download\t[=========      ]  38%") pra uma fração 0..1. dfu-util escreve
//  essas atualizações redesenhando a mesma linha de terminal com '\r'; se o
//  chamador nos entregar um pedaço bruto com várias atualizações coladas por
//  '\r' (em vez de uma por chamada), pegamos a ÚLTIMA "NN%" da string.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Text.RegularExpressions;

namespace DriveLab.Core.Update;

/// <summary>Stateless parser for dfu-util's stderr progress lines.</summary>
public static partial class DfuUtilProgress
{
    [GeneratedRegex(@"(\d{1,3})\s*%")]
    private static partial Regex PercentRegex();

    /// <summary>
    /// Extracts the last "NN%" found in <paramref name="line"/> as a 0..1 fraction, or
    /// null if the line carries no percentage (e.g. banner/status lines like
    /// "Opening DFU capable USB device..." or "Download done.").
    /// </summary>
    public static double? Parse(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        var matches = PercentRegex().Matches(line);
        if (matches.Count == 0)
            return null;

        var last = matches[^1];
        if (!int.TryParse(last.Groups[1].Value, out var percent))
            return null;

        percent = Math.Clamp(percent, 0, 100);
        return percent / 100.0;
    }
}
