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
}
