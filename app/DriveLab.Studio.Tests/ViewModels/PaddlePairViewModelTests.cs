using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class PaddlePairViewModelTests
{
    [Fact]
    public void Defaults_Are_Clutch_Combined_Digital()
    {
        var vm = new PaddlePairViewModel();
        Assert.Equal(PaddleFunction.Clutch, vm.Function);
        Assert.Equal(PaddleMode.Combined, vm.Mode);
        Assert.Equal(PaddleActuation.Digital, vm.Actuation);
        Assert.True(vm.ShowMode);
        Assert.True(vm.ShowBitePoint);
    }

    [Fact]
    public void Free_Function_Hides_Mode_And_BitePoint()
    {
        var vm = new PaddlePairViewModel();
        vm.SetFunctionCommand.Execute("Free");
        Assert.Equal(PaddleFunction.Free, vm.Function);
        Assert.False(vm.ShowMode);
        Assert.False(vm.ShowBitePoint);
    }

    [Fact]
    public void Independent_Mode_Hides_BitePoint_But_Keeps_Mode_Visible()
    {
        var vm = new PaddlePairViewModel();
        vm.SetModeCommand.Execute("Independent");
        Assert.Equal(PaddleMode.Independent, vm.Mode);
        Assert.True(vm.ShowMode);        // still a clutch
        Assert.False(vm.ShowBitePoint);  // but independent -> no bite point
    }

    [Fact]
    public void SetActuation_Parses_Enum()
    {
        var vm = new PaddlePairViewModel();
        vm.SetActuationCommand.Execute("Progression");
        Assert.Equal(PaddleActuation.Progression, vm.Actuation);
    }
}
