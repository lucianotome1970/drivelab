// ============================================================================
//  DriveLab
//  DiagnosticRecorder.cs — Grava telemetria de diagnóstico em CSV com marcações do usuário (loop de feedback FFB).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace DriveLab.Studio.Services;

/// <summary>Grava linhas de diagnóstico (timestamp + valores nomeados) num CSV, mais **marcações**
/// do usuário ("aqui ficou notchy") na mesma trilha de tempo. É o núcleo do loop de feedback do FFB:
/// o CSV é depois replicado no harness de host p/ afinar os parâmetros objetivamente. Recebe um
/// <see cref="TextWriter"/> (arquivo em produção, StringWriter em teste) → determinístico/testável.</summary>
public sealed class DiagnosticRecorder : System.IDisposable
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly TextWriter _writer;
    private readonly int _columnCount;

    public int RowCount { get; private set; }

    public DiagnosticRecorder(TextWriter writer, IReadOnlyList<string> columns)
    {
        _writer = writer;
        _columnCount = columns.Count;
        _writer.WriteLine("t_ms," + string.Join(",", columns.Select(Escape)) + ",mark");
    }

    /// <summary>Grava uma amostra: timestamp (ms) + os valores das colunas (+ marcação opcional).</summary>
    public void Record(double timestampMs, IReadOnlyList<double> values, string? mark = null)
    {
        var cells = values.Select(v => v.ToString("0.####", Inv));
        _writer.WriteLine($"{timestampMs.ToString("0.###", Inv)},{string.Join(",", cells)},{Escape(mark)}");
        RowCount++;
    }

    /// <summary>Anota um instante ("aqui tremeu") sem valores — linha de marcação na trilha de tempo.</summary>
    public void Mark(double timestampMs, string note)
    {
        var empty = string.Join(",", Enumerable.Repeat("", _columnCount));
        _writer.WriteLine($"{timestampMs.ToString("0.###", Inv)},{empty},{Escape(note)}");
        RowCount++;
    }

    /// <summary>Escapa um campo CSV (aspas se contiver vírgula/aspas/quebra de linha).</summary>
    private static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0
            ? "\"" + s.Replace("\"", "\"\"") + "\""
            : s;
    }

    public void Dispose() => _writer.Flush();
}
