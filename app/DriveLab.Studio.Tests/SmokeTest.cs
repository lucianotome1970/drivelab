using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests;

public class SmokeTest
{
    private sealed class SampleViewModel : ViewModelBase { }

    [Fact]
    public void ViewModelBase_Is_Usable_As_Base()
    {
        var vm = new SampleViewModel();
        Assert.IsAssignableFrom<ViewModelBase>(vm);
    }
}
