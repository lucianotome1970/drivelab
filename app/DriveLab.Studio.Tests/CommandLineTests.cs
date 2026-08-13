// ============================================================================
//  DriveLab
//  CommandLineTests.cs — Testes de detecção do flag --simulator na linha de comando.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Studio;
using Xunit;

namespace DriveLab.Studio.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData("/simulator")]
    [InlineData("--simulator")]
    [InlineData("-simulator")]
    [InlineData("/SIMULATOR")]
    public void Recognizes_Simulator_Flag(string arg)
    {
        Assert.True(CompositionRoot.IsSimulatorRequested(new[] { arg }));
    }

    [Fact]
    public void No_Flag_Means_Real_Hardware()
    {
        Assert.False(CompositionRoot.IsSimulatorRequested(Array.Empty<string>()));
        Assert.False(CompositionRoot.IsSimulatorRequested(new[] { "--other", "foo" }));
    }

    [Fact]
    public void Handles_Null_Args()
    {
        Assert.False(CompositionRoot.IsSimulatorRequested(null));
    }

    [Theory]
    [InlineData("/advanced")]
    [InlineData("--advanced")]
    [InlineData("-advanced")]
    [InlineData("--ADVANCED")]
    public void Recognizes_Advanced_Flag(string arg)
    {
        Assert.True(CompositionRoot.IsAdvancedMode(new[] { arg }));
    }

    [Fact]
    public void No_Advanced_Flag_Hides_Hardware()
    {
        // padrão (sem flag) = usuário final, aba Hardware escondida. Sem flag e sem arquivo marcador → false.
        Assert.False(CompositionRoot.IsAdvancedMode(Array.Empty<string>()));
        Assert.False(CompositionRoot.IsAdvancedMode(new[] { "--simulator" }));
        Assert.False(CompositionRoot.IsAdvancedMode(null));
    }
}
