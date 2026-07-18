// ============================================================================
//  DriveLab
//  DiagnosticRecorderTests.cs — Testes do gravador de diagnóstico CSV (linhas, marcações, escape).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.IO;
using DriveLab.Studio.Services;
using Xunit;

namespace DriveLab.Studio.Tests.Services;

public class DiagnosticRecorderTests
{
    [Fact]
    public void Writes_Header_Rows_And_Marks()
    {
        var sw = new StringWriter();
        using (var rec = new DiagnosticRecorder(sw, new[] { "pos", "torque" }))
        {
            rec.Record(0.0, new[] { 0.1, 0.5 });
            rec.Record(10.0, new[] { 0.2, -0.5 });
            rec.Mark(15.0, "notchy aqui");
            Assert.Equal(3, rec.RowCount);
        }

        var lines = sw.ToString().Trim().Replace("\r\n", "\n").Split('\n');
        Assert.Equal("t_ms,pos,torque,mark", lines[0]);
        Assert.Equal("0,0.1,0.5,", lines[1]);
        Assert.Equal("10,0.2,-0.5,", lines[2]);
        Assert.Equal("15,,,notchy aqui", lines[3]);   // marcação: valores vazios, nota na coluna mark
    }

    [Fact]
    public void Uses_Invariant_Decimal_And_Escapes_Commas()
    {
        var sw = new StringWriter();
        using (var rec = new DiagnosticRecorder(sw, new[] { "v" }))
        {
            rec.Record(1.5, new[] { 31.6 });                 // ponto decimal (invariant), não vírgula
            rec.Mark(2.0, "clipou, forte");                  // vírgula na nota → precisa aspas
        }

        var lines = sw.ToString().Trim().Replace("\r\n", "\n").Split('\n');
        Assert.Equal("1.5,31.6,", lines[1]);
        Assert.Equal("2,,\"clipou, forte\"", lines[2]);
    }
}
