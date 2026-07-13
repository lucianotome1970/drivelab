using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private static MainWindowViewModel New(out DashboardViewModel first, out DashboardViewModel second)
    {
        var session = new DeviceSession(new FakeTransport(), new ImmediateUiDispatcher());
        first = new DashboardViewModel(session);
        second = new DashboardViewModel(session);
        var pages = new[]
        {
            new NavItem("Dashboard", "\U0001F39B", first),
            new NavItem("Ajustes", "⚙", second),
        };
        return new MainWindowViewModel(session, new ConnectionViewModel(session), pages, new TestViewModel(session));
    }

    [Fact]
    public void CurrentPage_Defaults_To_First_Page()
    {
        var vm = New(out var first, out _);
        Assert.Same(first, vm.CurrentPage);
    }

    [Fact]
    public void Navigate_Switches_CurrentPage()
    {
        var vm = New(out _, out var second);
        var changed = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.CurrentPage)) changed = true; };

        vm.NavigateCommand.Execute(vm.Pages[1]);

        Assert.Same(second, vm.CurrentPage);
        Assert.True(changed);
    }

    [Fact]
    public void Exposes_Connection_And_Title()
    {
        var vm = New(out _, out _);
        Assert.NotNull(vm.Connection);
        Assert.Equal("DriveLab Studio", vm.Title);
    }

    [Fact]
    public void SimulatorMode_Defaults_False_And_Can_Be_Set()
    {
        var session = new DeviceSession(new FakeTransport(), new ImmediateUiDispatcher());
        var pages = new[] { new NavItem("Dashboard", "\U0001F39B", new DashboardViewModel(session)) };

        var real = new MainWindowViewModel(session, new ConnectionViewModel(session), pages, new TestViewModel(session));
        Assert.False(real.SimulatorMode);

        var sim = new MainWindowViewModel(session, new ConnectionViewModel(session), pages, new TestViewModel(session), simulatorMode: true);
        Assert.True(sim.SimulatorMode);
    }

    [Fact]
    public void CompositionRoot_Includes_Pedals_Page()
    {
        var vm = DriveLab.Studio.CompositionRoot.CreateMainWindowViewModel();
        Assert.Contains(vm.Pages, p => p.Label == "Pedais");
        vm.Dispose();
    }
}
