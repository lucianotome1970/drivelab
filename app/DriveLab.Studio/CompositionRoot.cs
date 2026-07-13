using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Hid;
using DriveLab.Simulator;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio;

public static class CompositionRoot
{
    // Todos os ajustes da base ficam numa página só ("Base do Volante").
    // O ângulo total de giro fica na página do Volante.
    private static readonly SettingId[] WheelBaseSettings =
    {
        // Força feedback
        SettingId.TotalStrength,
        SettingId.MaxTorqueLimit,
        SettingId.SoftStopStrength,
        SettingId.SoftStopRange,
        SettingId.SpringStrength,
        SettingId.DamperStrength,
        // Técnicos
        SettingId.StaticDamping,
        SettingId.ForceDirection,
        SettingId.PositionSmoothing,
        SettingId.PowerLimit,
        SettingId.BrakingLimit,
        SettingId.EncoderDirection,
        SettingId.EncoderCpr,
        SettingId.PolePairs,
        SettingId.CurrentP,
        SettingId.CurrentI,
        SettingId.CalibrationCurrent,
    };

    public static MainWindowViewModel CreateMainWindowViewModel(ITransport? transport = null, bool simulatorMode = false)
    {
        transport ??= new SimulatorTransport();
        var session = new DeviceSession(transport, new AvaloniaUiDispatcher());
        var connection = new ConnectionViewModel(session);

        var pages = new List<NavItem>
        {
            new("Volante", "\U0001F39B", new DashboardViewModel(session)),
            new("Base do Volante", "base", new SettingsGroupViewModel(session, "Base do Volante", WheelBaseSettings)),
            new("Telemetria", "📈", new TelemetryViewModel(session)),
        };

        // Teste (controle direto de força) não é uma aba: abre num modal à parte,
        // para o desenho do volante continuar visível ao fundo enquanto se ajusta a força.
        var test = new TestViewModel(session);

        return new MainWindowViewModel(session, connection, pages, test, simulatorMode);
    }

    /// <summary>Builds a transport talking to real hardware over USB HID (used when a device is present).</summary>
    public static ITransport CreateHidTransport() => new HidTransport(new HidSharpChannel());

    /// <summary>
    /// True when the app was launched with a simulator flag (/simulator, --simulator, -simulator).
    /// Sem a flag o app opera com hardware real.
    /// </summary>
    public static bool IsSimulatorRequested(string[]? args) =>
        args is not null && args.Any(a =>
            a.TrimStart('/', '-').Equals("simulator", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Detector de módulos do splash. Em modo simulador a base virtual conta como
    /// conectada; em modo real a sonda da base é stub (firmware em bring-up).
    /// </summary>
    public static StartupDetector CreateStartupDetector(bool simulatorMode) =>
        new(
            probeBase: () => Task.FromResult(simulatorMode || ProbeBaseHardware()),
            probePedals: () => Task.FromResult(false), // módulo de pedais em construção
            stepDelayMs: 450);

    // STUB: firmware em bring-up (M0), a base ainda não enumera por USB de forma estável.
    // Quando enumerar, trocar por:
    //   DeviceList.Local.GetHidDevices(DeviceIdentity.VendorId, DeviceIdentity.ProductId).Any()
    private static bool ProbeBaseHardware() => false;
}
