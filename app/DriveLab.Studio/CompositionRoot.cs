using DriveLab.Simulator;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio;

public static class CompositionRoot
{
    public static MainWindowViewModel CreateMainWindowViewModel()
    {
        var transport = new SimulatorTransport();
        var session = new DeviceSession(transport, new AvaloniaUiDispatcher());
        var connection = new ConnectionViewModel(session);

        var pages = new List<NavItem>
        {
            new("Dashboard", "\U0001F39B", new DashboardViewModel(session)),
            new("Ajustes", "⚙", new SettingsViewModel(session)),
        };

        return new MainWindowViewModel(session, connection, pages);
    }
}
