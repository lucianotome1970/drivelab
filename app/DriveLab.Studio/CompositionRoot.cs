using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Hid;
using DriveLab.Simulator;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio;

public static class CompositionRoot
{
    // "Base do Volante" com abas (o ângulo total de giro fica na página do Volante).
    private static readonly SettingsTabSpec[] WheelBaseTabs =
    {
        new("Basic", new[]
        {
            SettingId.TotalStrength,
            SettingId.SoftStopStrength,
            SettingId.SoftStopRange,
            SettingId.SpringStrength,
            SettingId.DamperStrength,
        }),
        new("Advanced", new[]
        {
            SettingId.StaticDamping,
            SettingId.MaxTorqueLimit,
            SettingId.ForceDirection,
            SettingId.PositionSmoothing,
            SettingId.PowerLimit,
            SettingId.BrakingLimit,
        }),
        new("Hardware", new[]
        {
            SettingId.EncoderDirection,
            SettingId.EncoderCpr,
            SettingId.PolePairs,
            SettingId.CurrentP,
            SettingId.CurrentI,
            SettingId.CalibrationCurrent,
        }),
    };

    public static MainWindowViewModel CreateMainWindowViewModel(ITransport? transport = null, bool simulatorMode = false)
    {
        transport ??= new SimulatorTransport();
        var dispatcher = new AvaloniaUiDispatcher();
        var session = new DeviceSession(transport, dispatcher);
        var connection = new ConnectionViewModel(session);

        var pedalSession = new PedalDeviceSession(new SimulatorPedalTransport(), dispatcher);
        var pedals = new PedalsViewModel(pedalSession, new JsonPedalProfileStorage());

        // Base do Volante: abas de settings + Telemetria como última aba.
        var wheelBaseTabs = WheelBaseTabs
            .Select(t => new PageTab(t.Header, new SettingsGroupViewModel(session, t.Header, t.Ids)))
            .Append(new PageTab("Telemetria", new TelemetryViewModel(session)))
            .ToList();

        var pages = new List<NavItem>
        {
            new("Volante", "\U0001F39B", new DashboardViewModel(session)),
            new("Base do Volante", "base", new SettingsPageViewModel("Base do Volante", wheelBaseTabs)),
            new("Pedais", "\U0001F9B6", pedals),
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
