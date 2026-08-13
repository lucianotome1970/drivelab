using System;
using System.IO;
using DriveLab.Studio.Services;
using Xunit;

namespace DriveLab.Studio.Tests.Services;

public class CrashLoggerTests
{
    [Fact]
    public void WriteTo_Appends_Context_And_Exception()
    {
        var path = Path.Combine(Path.GetTempPath(), $"crash-{Guid.NewGuid():N}.log");
        try
        {
            CrashLogger.WriteTo(path, "TesteCtx", new InvalidOperationException("boom-xyz"));
            CrashLogger.WriteTo(path, "Segundo", new Exception("outro-erro"));

            var text = File.ReadAllText(path);
            Assert.Contains("TesteCtx", text);
            Assert.Contains("boom-xyz", text);
            Assert.Contains("Segundo", text);     // acrescenta, não sobrescreve
            Assert.Contains("outro-erro", text);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void WriteTo_Never_Throws_On_Bad_Path()
    {
        // Caminho inválido não pode lançar (logar nunca é fatal).
        CrashLogger.WriteTo("/\0/caminho/invalido.log", "Ctx", new Exception("x"));
    }
}
