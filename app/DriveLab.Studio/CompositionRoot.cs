using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Hid;
using DriveLab.Hid.Simagic;
using DriveLab.Simulator;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;
using L = DriveLab.Studio.Localization.LocalizationManager;

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
        var connection = new ConnectionViewModel(session, dispatcher);

        // Modo real: conexão automática (hotplug) — sem botão Conectar. Detecta o cabo USB da
        // base (VID/PID) e conecta/desconecta sozinho. No simulador a conexão é manual (botão).
        DeviceAutoConnector? autoConnector = null;
        if (!simulatorMode)
        {
            autoConnector = new DeviceAutoConnector(session, HidTransport.IsDevicePresent);
            autoConnector.Start();
        }

        // Autodetecção (rótulo genérico, sem expor marca/modelo):
        //  1) nossa pedaleira (P0, configurável) → 2) Simagic (leitura) → 3) simulador.
        var simagicReader = new SimagicHidSharpReader();
        PedalDeviceSession pedalSession;
        if (HidPedalTransport.IsDevicePresent())
            pedalSession = new PedalDeviceSession(new HidPedalTransport(new HidSharpChannel()), dispatcher, L.Get("Pedal_Source_Detected"));
        else if (simagicReader.IsPresent())
            pedalSession = new PedalDeviceSession(new SimagicPedalTransport(simagicReader), dispatcher, L.Get("Pedal_Source_Detected"));
        else
            pedalSession = new PedalDeviceSession(new SimulatorPedalTransport(), dispatcher, L.Get("Pedal_Source_Simulator"));
        var pedals = new PedalsViewModel(pedalSession, new JsonPedalProfileStorage());

        // Freio de mão: autodetecção 1) HID 0x0003 → 2) simulador (rótulo genérico).
        HandbrakeDeviceSession handbrakeSession;
        if (HidHandbrakeTransport.IsDevicePresent())
            handbrakeSession = new HandbrakeDeviceSession(new HidHandbrakeTransport(new HidSharpChannel()), dispatcher, L.Get("Pedal_Source_Detected"));
        else
            handbrakeSession = new HandbrakeDeviceSession(new SimulatorHandbrakeTransport(), dispatcher, L.Get("Pedal_Source_Simulator"));
        var handbrake = new HandbrakeViewModel(handbrakeSession, new JsonHandbrakeProfileStorage());

        // Base do Volante: abas de settings + Telemetria como última aba.
        var wheelBaseTabs = WheelBaseTabs
            .Select(t => new PageTab(L.Get($"Tab_{t.Header}"), new SettingsGroupViewModel(session, t.Header, t.Ids)))
            .Append(new PageTab(L.Get("Tab_Telemetry"), new TelemetryViewModel(session)))
            .ToList();

        // Home (dash): card do volante + card da base (força total) + resumo ao vivo
        // dos pedais e do freio de mão, lado a lado. Base usa a MESMA sessão do volante.
        var home = new HomeViewModel(new DashboardViewModel(session), pedals, handbrake, new BaseViewModel(session));

        var wheel = new WheelViewModel(new JsonWheelProfileStorage(), simulatorMode);

        var pages = new List<NavItem>
        {
            new(L.Get("Nav_Home"), "\U0001F39B", home),
            new(L.Get("Nav_WheelBase"), "base", new SettingsPageViewModel(session, L.Get("Page_WheelBase"), wheelBaseTabs)),
            new(L.Get("Nav_Pedals"), "\U0001F9B6", pedals),
            new(L.Get("Nav_Handbrake"), "handbrake", handbrake),
            new(L.Get("Nav_Wheel"), "wheel", wheel),
        };

        // Teste (controle direto de força) não é uma aba: abre num modal à parte,
        // para o desenho do volante continuar visível ao fundo enquanto se ajusta a força.
        var test = new TestViewModel(session);

        return new MainWindowViewModel(session, connection, pages, test, simulatorMode, autoConnector);
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
