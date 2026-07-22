// ============================================================================
//  DriveLab
//  CompositionRoot.cs — Monta o grafo de dependências do app (sessões, view models, auto-connect e páginas).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Core.Update;
using DriveLab.Hid;
using DriveLab.Hid.Simagic;
using DriveLab.Hid.Update;
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
            BaseSettingId.TotalStrength,
            BaseSettingId.SoftStopStrength,
            BaseSettingId.SoftStopRange,
            BaseSettingId.SpringStrength,
            BaseSettingId.DamperStrength,
        }),
        new("Advanced", new[]
        {
            BaseSettingId.StaticDamping,
            BaseSettingId.MaxTorqueLimit,
            BaseSettingId.ForceDirection,
            BaseSettingId.PositionSmoothing,
            BaseSettingId.PowerLimit,
            BaseSettingId.BrakingLimit,
            BaseSettingId.ReconstructionSteps,
            BaseSettingId.ReconstructionLpf,
            BaseSettingId.OutputFilterHz,
            BaseSettingId.OscGuardEnable,
            BaseSettingId.EndstopDamping,
            BaseSettingId.Linearity,
            BaseSettingId.CoggingEnable,
            BaseSettingId.SlewRate,
        }),
        new("Hardware", new[]
        {
            BaseSettingId.EncoderDirection,
            BaseSettingId.EncoderCpr,
            BaseSettingId.EncoderType,
            BaseSettingId.PolePairs,
            BaseSettingId.CurrentP,
            BaseSettingId.CurrentI,
            BaseSettingId.CalibrationCurrent,
        }),
    };

    public static MainWindowViewModel CreateMainWindowViewModel(IBaseTransport? transport = null, bool simulatorMode = false)
    {
        transport ??= new SimulatorBaseTransport();
        var dispatcher = new AvaloniaUiDispatcher();
        var session = new BaseSession(transport, dispatcher);
        var connection = new ConnectionViewModel(session, dispatcher);

        // Conexão: no SIMULADOR é manual (botão Conectar); no REAL é automática (hotplug USB) para
        // TODOS os dispositivos — o DeviceAutoConnector faz polling da presença e conecta/desconecta.
        var autoConnectors = new List<IDisposable>();

        // Base (topo): o transporte já vem por modo do App (simulador vs HID). Real → auto-connect.
        // Guardamos o auto-connector da base para o coordenador de update pausá-lo/retomá-lo.
        IDeviceAccessCoordinator? updateCoordinator = null;
        if (!simulatorMode)
        {
            var baseAutoConnect = StartAutoConnect(
                () => session.IsConnected, session.ConnectAsync, session.DisconnectAsync,
                HidBaseTransport.IsDevicePresent, dispatcher);
            autoConnectors.Add(baseAutoConnect);
            updateCoordinator = new HostDeviceAccessCoordinator(baseAutoConnect, session.DisconnectAsync);
        }

        // Pedais: simulador → simulador; real → nossa pedaleira P0 (ou Simagic, leitura) + hotplug.
        var simagicReader = new SimagicHidSharpReader();
        PedalDeviceSession pedalSession;
        Func<bool>? pedalPresent = null;
        bool pedalIsRp2040 = false; // só nossa pedaleira P0 (HID) aceita EnterBootloader/atualização UF2.
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
            pedalIsRp2040 = true;
        }
        var pedals = new PedalsViewModel(pedalSession, new JsonPedalProfileStorage(), simulatorMode,
            new JsonNamedProfileStore<PedalProfile>("pedals"));
        if (pedalPresent is not null)
            autoConnectors.Add(StartAutoConnect(
                () => pedals.IsConnected,
                () => pedals.ConnectCommand.ExecuteAsync(null),
                () => pedals.DisconnectCommand.ExecuteAsync(null),
                pedalPresent, dispatcher));

        // Freio de mão: simulador → simulador; real → HID 0x0003 + hotplug.
        HandbrakeDeviceSession handbrakeSession;
        Func<bool>? handbrakePresent = null;
        bool handbrakeIsRp2040 = false; // só o freio de mão HID real aceita EnterBootloader/atualização UF2.
        if (simulatorMode)
            handbrakeSession = new HandbrakeDeviceSession(new SimulatorHandbrakeTransport(), dispatcher, L.Get("Pedal_Source_Simulator"));
        else
        {
            handbrakeSession = new HandbrakeDeviceSession(new HidHandbrakeTransport(new HidSharpChannel()), dispatcher, L.Get("Handbrake_Source_Detected"));
            handbrakePresent = HidHandbrakeTransport.IsDevicePresent;
            handbrakeIsRp2040 = true;
        }
        var handbrake = new HandbrakeViewModel(handbrakeSession, new JsonHandbrakeProfileStorage(), simulatorMode,
            new JsonNamedProfileStore<HandbrakeProfile>("handbrake"));
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

        // Volante (aro removível): simulador → sessão simulada (botão Conectar); real → HID 0x0004
        // + hotplug + LED ao vivo. Criado ANTES do dash p/ o card "Volante" acender pela conexão do aro.
        WheelDeviceSession wheelSession;
        Func<bool>? wheelPresent = null;
        bool wheelIsRp2040 = false; // só o aro HID real aceita EnterBootloader/atualização UF2.
        if (simulatorMode)
        {
            wheelSession = new WheelDeviceSession(new SimulatorWheelTransport(), dispatcher, L.Get("Pedal_Source_Simulator"));
        }
        else
        {
            wheelSession = new WheelDeviceSession(new HidWheelTransport(new HidSharpChannel()), dispatcher, L.Get("Wheel_Source_Detected"));
            wheelPresent = HidWheelTransport.IsDevicePresent;
            wheelIsRp2040 = true;
        }

        // Home (dash): card do volante (acende com o aro) + card da base (força total) + resumo
        // ao vivo dos pedais e do freio de mão. Base usa a MESMA sessão FFB.
        var home = new HomeViewModel(new DashboardViewModel(session, wheelSession), pedals, handbrake, new BaseViewModel(session));

        // Rev-lights por telemetria de jogo: serviço que lê a telemetria (ACC/AC hoje; iRacing/rF2 depois) e
        // dirige a barra de LEDs do aro. Relógio monotônico compartilhado (varredura simulada + piscar do shift).
        // Só envia quando o aro está conectado; degrada em silêncio sem jogo/fora do Windows.
        var revClock = System.Diagnostics.Stopwatch.StartNew();
        var revService = new DriveLab.Core.Telemetry.GameTelemetryService(
            new DriveLab.Core.Telemetry.IGameTelemetrySource[]
            {
                new DriveLab.Core.Telemetry.Sources.AccSharedMemorySource(),
            },
            report => wheelSession.IsConnected ? wheelSession.SendLedAsync(report) : Task.CompletedTask,
            () => revClock.Elapsed.TotalSeconds);
        // Efeitos de FFB por telemetria (ABS/slip/marcha/RPM): o mesmo tick calcula a força aditiva e a manda
        // à BASE pelo report DIRECT (o firmware soma via engine.setTelemetryForce). Só chega força se a base
        // estiver conectada; no motor OFF aparece no medidor de clipping, no Stage 1 vira força real.
        revService.EffectMixer = DriveLab.Core.Telemetry.Effects.TelemetryEffectMixer.CreateDefault();
        revService.SendTelemetryForce = f => session.SendDirectControlAsync(
            new BaseDirectControl { TelemetryForce = (short)System.Math.Clamp(f, -255f, 255f) });

        var wheel = new WheelViewModel(new JsonWheelProfileStorage(), simulatorMode, wheelSession,
            new JsonNamedProfileStore<WheelProfile>("wheel"), revService);
        wheel.RevLights = new RevLightsViewModel(revService, dispatcher,
            () => revClock.Elapsed.TotalSeconds, wheel.PushLedsNow);
        wheel.TelemetryEffects = new TelemetryEffectsViewModel(revService, dispatcher);
        if (wheelPresent is not null)
        {
            System.Console.WriteLine($"[DriveLab] Volante presente no arranque (HidSharp 0x1209:0x0004): {wheelPresent()}");
            autoConnectors.Add(StartAutoConnect(
                () => wheel.IsConnected,
                () => wheel.ConnectCommand.ExecuteAsync(null),
                () => wheel.DisconnectCommand.ExecuteAsync(null),
                wheelPresent, dispatcher));
        }
        var basePage = new SettingsPageViewModel(session, L.Get("Page_WheelBase"), wheelBaseTabs,
            new JsonNamedProfileStore<BaseProfile>("base"));

        // Atualização de firmware: por enquanto só a base, usando o MESMO transporte da sessão
        // (real → HID; simulador → transporte simulado, EnterDfu vira no-op).
        // Diagnóstico (só no modo real): cada poll do `dfu-util -l` vai para um log, para depurar
        // na bancada por que o bootloader (não) apareceu. Best-effort — nunca deixa o update falhar.
        Action<string>? dfuLog = simulatorMode ? null : BuildDfuDebugLog();
        var updateDevices = new List<IDeviceUpdater> { new BaseUpdater(transport, diagnostics: dfuLog) };

        // RP2040 (pedal/handbrake/aro): só entram no dropdown de update quando o transporte é o
        // HID real — o EnterBootloader (0x5A) é enviado pela própria sessão. No simulador/Simagic
        // não há BOOTSEL a acionar, então pulamos.
        if (pedalIsRp2040)
            updateDevices.Add(new Rp2040Updater(DeviceKind.Pedal,
                () => pedalSession.SendCommandAsync(PedalCommandId.EnterBootloader)));
        if (handbrakeIsRp2040)
            updateDevices.Add(new Rp2040Updater(DeviceKind.Handbrake,
                () => handbrakeSession.SendCommandAsync(PedalCommandId.EnterBootloader)));
        if (wheelIsRp2040)
            updateDevices.Add(new Rp2040Updater(DeviceKind.Wheel,
                () => wheelSession.SendCommandAsync(WheelCommandId.EnterBootloader)));

        // Cliente de releases do GitHub (auto-update): HttpClient com User-Agent (exigido pela API do GitHub).
        var releasesHttp = new System.Net.Http.HttpClient();
        releasesHttp.DefaultRequestHeaders.UserAgent.ParseAdd("DriveLab-Studio");
        var releaseClient = new DriveLab.Core.Update.GitHubReleaseClient(uri => releasesHttp.GetStringAsync(uri));
        var update = new UpdateViewModel(updateDevices, coordinator: updateCoordinator, baseSession: session,
            releaseClient: releaseClient, downloadBytes: uri => releasesHttp.GetByteArrayAsync(uri),
            deviceStatus: kind => kind switch
            {
                DeviceKind.Pedal => (pedalSession.IsConnected, pedalSession.FirmwareVersion),
                DeviceKind.Handbrake => (handbrakeSession.IsConnected, handbrakeSession.FirmwareVersion),
                DeviceKind.Wheel => (wheelSession.IsConnected, wheelSession.FirmwareVersion),
                _ => (session.IsConnected, session.FirmwareVersion),   // Base
            });
        // Selo do módulo Update reflete o dispositivo SELECIONADO: refresca quando qualquer módulo conecta/
        // desconecta (a base já é escutada dentro do VM). Evita mostrar "sem conexão" da base ao atualizar o pedal.
        void RefreshUpdateStatus(object? s, EventArgs e) => dispatcher.Post(update.RefreshStatus);
        pedalSession.Connected += RefreshUpdateStatus; pedalSession.Disconnected += RefreshUpdateStatus;
        handbrakeSession.Connected += RefreshUpdateStatus; handbrakeSession.Disconnected += RefreshUpdateStatus;
        wheelSession.Connected += RefreshUpdateStatus; wheelSession.Disconnected += RefreshUpdateStatus;

        // Auto-perfil por jogo: detecta o sim rodando (processo) e carrega o perfil casado em cada módulo,
        // setando ProfileLibrary.SelectedName (que já aplica). Mapa persistido em JSON.
        var autoProfileStore = new JsonGameProfileMapStore();
        var autoProfileMap = autoProfileStore.Load();
        // Catálogo resolvido a cada detecção = embutido + jogos que o usuário adicionou (valem na hora).
        var gameDetector = new DriveLab.Core.Games.GameDetector(
            () => System.Diagnostics.Process.GetProcesses().Select(p => p.ProcessName).ToList(),
            () => DriveLab.Core.Games.GameCatalog.WithCustom(autoProfileMap.CustomGames));
        var autoProfileService = new AutoProfileService(gameDetector, () => autoProfileMap, dispatcher,
            name => basePage.ProfileLibrary.SelectedName = name,
            name => wheel.ProfileLibrary.SelectedName = name,
            name => pedals.ProfileLibrary.SelectedName = name,
            name => handbrake.ProfileLibrary.SelectedName = name);
        var autoProfile = new AutoProfileViewModel(autoProfileService, autoProfileMap, autoProfileStore, dispatcher,
            basePage.ProfileLibrary.Profiles, wheel.ProfileLibrary.Profiles,
            pedals.ProfileLibrary.Profiles, handbrake.ProfileLibrary.Profiles);
        // Guarda: uma troca automática não pode descartar ajustes não salvos de nenhum módulo.
        autoProfileService.HasUnsavedChanges = () =>
            basePage.ProfileLibrary.IsModified || wheel.ProfileLibrary.IsModified ||
            pedals.ProfileLibrary.IsModified || handbrake.ProfileLibrary.IsModified;
        autoProfileService.Enabled = autoProfileMap.Enabled;
        if (autoProfileMap.Enabled) autoProfileService.Start();

        // Exportar/importar perfis (arquivo .json) — habilitado nos 4 módulos.
        var profilePicker = new AvaloniaProfileFilePicker();
        basePage.ProfileLibrary.EnableFileExchange(profilePicker, "base");
        wheel.ProfileLibrary.EnableFileExchange(profilePicker, "wheel");
        pedals.ProfileLibrary.EnableFileExchange(profilePicker, "pedals");
        handbrake.ProfileLibrary.EnableFileExchange(profilePicker, "handbrake");

        var pages = new List<NavItem>
        {
            // Ordem da sidebar: Home · Base · Volante · Pedais · Freio de mão · Atualizar firmware · Auto-perfil
            // (o volante vem logo após a base). Os ícones em MainWindow.axaml seguem estes índices.
            new(L.Get("Nav_Home"), "\U0001F39B", home),
            new(L.Get("Nav_WheelBase"), "base", basePage, L.Get("Page_WheelBase")),
            new(L.Get("Nav_Wheel"), "wheel", wheel, L.Get("Wheel_Config")),
            new(L.Get("Nav_Pedals"), "\U0001F9B6", pedals, L.Get("Pedal_Title")),
            new(L.Get("Nav_Handbrake"), "handbrake", handbrake, L.Get("Handbrake_Title")),
            new(L.Get("Nav_Update"), "update", update, L.Get("Update_Title")),
            new(L.Get("Nav_AutoProfile"), "autoprofile", autoProfile, L.Get("AutoProfile_Title")),
        };

        // Teste (controle direto de força) não é uma aba: abre num modal à parte,
        // para o desenho do volante continuar visível ao fundo enquanto se ajusta a força.
        var test = new TestViewModel(session);

        var main = new MainWindowViewModel(session, connection, pages, test, simulatorMode, autoConnectors);

        // Clique num card do dash (dispositivo detectado) navega para a página do módulo.
        home.ModuleNavigator = key =>
        {
            ViewModelBase? target = key switch
            {
                "pedals" => pedals,
                "handbrake" => handbrake,
                "wheel" => wheel,
                "base" => basePage,
                _ => null,
            };
            var nav = pages.FirstOrDefault(p => ReferenceEquals(p.Page, target));
            if (nav is not null)
                main.SelectedPage = nav;
        };

        return main;
    }

    /// <summary>Build a best-effort file logger for DFU update diagnostics (`~/DriveLab-dfu-debug.log`).
    /// Each line is timestamped; IO errors are swallowed so logging can never break an update.</summary>
    private static Action<string> BuildDfuDebugLog()
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "DriveLab-dfu-debug.log");
        return line =>
        {
            try { File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {line}{Environment.NewLine}"); }
            catch { /* best-effort: diagnóstico nunca pode derrubar o update */ }
        };
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
    public static IBaseTransport CreateHidTransport() => new HidBaseTransport(new HidSharpChannel());

    /// <summary>
    /// True when the app was launched with a simulator flag (/simulator, --simulator, -simulator).
    /// Sem a flag o app opera com hardware real.
    /// </summary>
    public static bool IsSimulatorRequested(string[]? args) =>
        args is not null && args.Any(a =>
            a.TrimStart('/', '-').Equals("simulator", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Detector de módulos do splash. Em modo simulador a base virtual conta como
    /// conectada; em modo real a sonda usa presença HID real (canal A0, vendor 0xFF00).
    /// </summary>
    public static StartupDetector CreateStartupDetector(bool simulatorMode) =>
        new(
            probeBase: () => Task.FromResult(simulatorMode || ProbeBaseHardware()),
            probePedals: () => Task.FromResult(false), // módulo de pedais em construção
            stepDelayMs: 450);

    private static bool ProbeBaseHardware() => HidBaseTransport.IsDevicePresent();
}
