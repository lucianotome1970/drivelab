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
        };

        // Teste (controle direto de força) não é uma aba: abre num modal à parte,
        // para o desenho do volante continuar visível ao fundo enquanto se ajusta a força.
        var test = new TestViewModel(session);

        return new MainWindowViewModel(session, connection, pages, test);
    }

    /// <summary>Builds a transport talking to real hardware over USB HID (used when a device is present).</summary>
    public static ITransport CreateHidTransport() => new HidTransport(new HidSharpChannel());
}
