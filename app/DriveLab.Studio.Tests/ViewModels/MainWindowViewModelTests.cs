using Xunit;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private static MainWindowViewModel New()
    {
        var session = new DeviceSession(new FakeTransport(), new ImmediateUiDispatcher());
        return new MainWindowViewModel(new ConnectionViewModel(session), new DashboardViewModel(session));
    }

    [Fact]
    public void CurrentPage_Defaults_To_Dashboard()
    {
        var vm = New();
        Assert.IsType<DashboardViewModel>(vm.CurrentPage);
    }

    [Fact]
    public void Exposes_Connection_And_Title()
    {
        var vm = New();
        Assert.NotNull(vm.Connection);
        Assert.Equal("DriveLab Studio", vm.Title);
    }
}
