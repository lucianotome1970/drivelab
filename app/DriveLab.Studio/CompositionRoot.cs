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
            SettingId.EncoderType,
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

        // Conexão: no SIMULADOR é manual (botão Conectar); no REAL é automática (hotplug USB) para
        // TODOS os dispositivos — o DeviceAutoConnector faz polling da presença e conecta/desconecta.
        var autoConnectors = new List<IDisposable>();

        // Base (topo): o transporte já vem por modo do App (simulador vs HID). Real → auto-connect.
        if (!simulatorMode)
            autoConnectors.Add(StartAutoConnect(
                () => session.IsConnected, session.ConnectAsync, session.DisconnectAsync,
                HidTransport.IsDevicePresent, dispatcher));

        // Pedais: simulador → simulador; real → nossa pedaleira P0 (ou Simagic, leitura) + hotplug.
        var simagicReader = new SimagicHidSharpReader();
        PedalDeviceSession pedalSession;
        Func<bool>? pedalPresent = null;
        if (simulatorMode)
            pedalSession = new PedalDeviceSession(new SimulatorPedalTransport(), dispatcher, L.Get("Pedal_Source_Simulator"));
        else if (simagicReader.IsPresent() && !HidPedalTransport.IsDevicePresent())
        {
            pedalSession = new PedalDeviceSession(new SimagicPedalTransport(simagicReader), dispatcher, L.Get("Pedal_Source_Detected"));
            pedalPresent = simagicReader.IsPresent;
        }
        else
        {
            pedalSession = new PedalDeviceSession(new HidPedalTransport(new HidSharpChannel()), dispatcher, L.Get("Pedal_Source_Detected"));
            pedalPresent = HidPedalTransport.IsDevicePresent;
        }
        var pedals = new PedalsViewModel(pedalSession, new JsonPedalProfileStorage(), simulatorMode);
        if (pedalPresent is not null)
            autoConnectors.Add(StartAutoConnect(
                () => pedals.IsConnected,
                () => pedals.ConnectCommand.ExecuteAsync(null),
                () => pedals.DisconnectCommand.ExecuteAsync(null),
                pedalPresent, dispatcher));

        // Freio de mão: simulador → simulador; real → HID 0x0003 + hotplug.
        HandbrakeDeviceSession handbrakeSession;
        Func<bool>? handbrakePresent = null;
        if (simulatorMode)
            handbrakeSession = new HandbrakeDeviceSession(new SimulatorHandbrakeTransport(), dispatcher, L.Get("Pedal_Source_Simulator"));
        else
        {
            handbrakeSession = new HandbrakeDeviceSession(new HidHandbrakeTransport(new HidSharpChannel()), dispatcher, L.Get("Pedal_Source_Detected"));
            handbrakePresent = HidHandbrakeTransport.IsDevicePresent;
        }
        var handbrake = new HandbrakeViewModel(handbrakeSession, new JsonHandbrakeProfileStorage(), simulatorMode);
        if (handbrakePresent is not null)
            autoConnectors.Add(StartAutoConnect(
                () => handbrake.IsConnected,
                () => handbrake.ConnectCommand.ExecuteAsync(null),
                () => handbrake.DisconnectCommand.ExecuteAsync(null),
                handbrakePresent, dispatcher));

        // Base do Volante: abas de settings + Telemetria como última aba.
        var wheelBaseTabs = WheelBaseTabs
            .Select(t => new PageTab(L.Get($"Tab_{t.Header}"), t.Header == "Hardware"
                ? new HardwareTabViewModel(session, t.Header, t.Ids)
                : new SettingsGroupViewModel(session, t.Header, t.Ids)))
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

        return new MainWindowViewModel(session, connection, pages, test, simulatorMode, autoConnectors);
    }

    private static DeviceAutoConnector StartAutoConnect(
        Func<bool> isConnected, Func<Task> connect, Func<Task> disconnect,
        Func<bool> isPresent, IUiDispatcher dispatcher)
    {
        var connector = new DeviceAutoConnector(isConnected, connect, disconnect, isPresent, dispatcher);
        connector.Start();
        return connector;
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
