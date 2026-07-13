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
        var dashboard = new DashboardViewModel(session);
        return new MainWindowViewModel(connection, dashboard);
    }
}
