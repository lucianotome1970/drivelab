using DriveLab.Core.Transport;
using DriveLab.Hid;
using DriveLab.Simulator;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio;

public static class CompositionRoot
{
    public static MainWindowViewModel CreateMainWindowViewModel(ITransport? transport = null)
    {
        transport ??= new SimulatorTransport();
        var session = new DeviceSession(transport, new AvaloniaUiDispatcher());
        var connection = new ConnectionViewModel(session);

        var pages = new List<NavItem>
        {
            new("Dashboard", "\U0001F39B", new DashboardViewModel(session)),
            new("Ajustes", "⚙", new SettingsViewModel(session)),
            new("Telemetria", "📈", new TelemetryViewModel(session)),
            new("Teste", "🕹", new TestViewModel(session)),
        };

        return new MainWindowViewModel(session, connection, pages);
    }

    /// <summary>Builds a transport talking to real hardware over USB HID (used when a device is present).</summary>
    public static ITransport CreateHidTransport() => new HidTransport(new HidSharpChannel());
}
