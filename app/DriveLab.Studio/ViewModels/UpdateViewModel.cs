// ============================================================================
//  DriveLab
//  UpdateViewModel.cs — VM da tela de atualização de firmware por USB:
//  escolher o dispositivo, selecionar o arquivo, validar a assinatura contra
//  o dispositivo e disparar o fluxo EnterDfu → WaitForBootloader → Flash,
//  reportando progresso e um status final amigável.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Update;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>VM do módulo "Atualizar firmware". Recebe a lista de <see cref="IDeviceUpdater"/>
/// disponíveis (hoje só a base) já ligados aos transportes reais; o seletor de arquivo e a
/// leitura de bytes do disco são injetados para manter <see cref="SelectFileAsync"/> e
/// <see cref="SendAsync"/> testáveis sem tocar IO/UI real.</summary>
public sealed partial class UpdateViewModel : ViewModelBase
{
    /// <summary>Janela para o salto automático (por software) trazer o bootloader. Ele sobe em ~2-3s
    /// ou não sobe — não adianta esperar muito.</summary>
    private static readonly TimeSpan AutoBootloaderTimeout = TimeSpan.FromSeconds(8);

    /// <summary>Janela para a etapa manual (SW1→DFU + power-cycle): a placa já deve estar em DFU quando
    /// o usuário clica Continuar, então uma checagem curta basta.</summary>
    private static readonly TimeSpan ManualBootloaderTimeout = TimeSpan.FromSeconds(10);

    private readonly IFilePicker _filePicker;
    private readonly Func<string, Task<byte[]>> _readFile;
    private readonly IDeviceAccessCoordinator? _coordinator;
    private readonly BaseSession? _baseSession;
    private readonly GitHubReleaseClient? _releaseClient;
    private readonly Func<Uri, Task<byte[]>>? _downloadBytes;
    private GitHubAsset? _pendingAsset;   // asset do último "verificar" p/ o "baixar e usar"
    private readonly Func<DeviceKind, (bool Connected, FirmwareVersion Version)>? _deviceStatus;

    // Estado que persiste entre Send (tentativa auto) e Continuar/Cancelar (etapa manual SW1→DFU):
    // qual dispositivo está sendo atualizado e se o acesso exclusivo à USB ainda está retido.
    private IDeviceUpdater? _inFlightDevice;
    private bool _exclusiveHeld;

    /// <summary>Placa detectada + versão do firmware que está rodando nela (da telemetria 0x21), ou
    /// "nenhuma placa detectada". Ajuda o usuário a ver de qual versão ele está atualizando.</summary>
    [ObservableProperty]
    private string _connectedDeviceInfo = "Nenhuma placa detectada.";

    public IReadOnlyList<IDeviceUpdater> Devices { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private IDeviceUpdater? _selectedDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFirmwarePath))]
    private string _firmwarePath = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isFirmwareValid;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidationMessage))]
    private string _validationMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatusMessage))]
    private string _statusMessage = "";

    /// <summary>Bindings de visibilidade (Avalonia negacao "!" so funciona bem em bool).</summary>
    public bool HasFirmwarePath => !string.IsNullOrEmpty(FirmwarePath);
    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);
    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    /// <summary>Resultado do "verificar atualizações" no GitHub (última versão / nova disponível / erro).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdateCheckMessage))]
    private string _updateCheckMessage = "";
    public bool HasUpdateCheckMessage => !string.IsNullOrEmpty(UpdateCheckMessage);
    public bool CanCheckUpdates => _releaseClient is not null;

    /// <summary>Há um asset baixável do último "verificar" (habilita "Baixar e usar").</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadUpdateCommand))]
    private bool _updateDownloadable;

    [ObservableProperty] private double _progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueDfuCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelDfuCommand))]
    private bool _isSending;

    /// <summary>True quando o salto automático falhou e estamos esperando o usuário fazer SW1→DFU +
    /// power-cycle e clicar Continuar. Controla a visibilidade do painel de instrução manual.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueDfuCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelDfuCommand))]
    private bool _needsManualDfu;

    /// <summary>Instruções da etapa manual, específicas do dispositivo (RP2040 = BOOTSEL; base = SW1→DFU).</summary>
    [ObservableProperty] private string _manualInstructions = "";

    public UpdateViewModel(IReadOnlyList<IDeviceUpdater> devices, IFilePicker? filePicker = null,
        Func<string, Task<byte[]>>? readFile = null, IDeviceAccessCoordinator? coordinator = null,
        BaseSession? baseSession = null, GitHubReleaseClient? releaseClient = null,
        Func<Uri, Task<byte[]>>? downloadBytes = null,
        Func<DeviceKind, (bool, FirmwareVersion)>? deviceStatus = null,
        TimeSpan? bootloaderPollInterval = null,
        IUiDispatcher? uiDispatcher = null)
    {
        _bootloaderPollInterval = bootloaderPollInterval ?? TimeSpan.FromSeconds(3);
        _uiDispatcher = uiDispatcher;
        Devices = devices;
        _filePicker = filePicker ?? new AvaloniaFilePicker();
        _readFile = readFile ?? (path => File.ReadAllBytesAsync(path));
        _coordinator = coordinator;
        _baseSession = baseSession;
        _releaseClient = releaseClient;
        _downloadBytes = downloadBytes;
        _deviceStatus = deviceStatus;
        _selectedDevice = devices.Count > 0 ? devices[0] : null;

        if (_baseSession is not null)
        {
            // A base conecta/desconecta sozinha (auto-connect) e a versão chega na 1ª telemetria —
            // por isso escutamos os 3 eventos e recalculamos o rótulo a cada um.
            _baseSession.Connected += OnBaseConnectionChanged;
            _baseSession.Disconnected += OnBaseConnectionChanged;
            _baseSession.StateReceived += OnBaseStateReceived;
        }
        RefreshStatus();
        RestartBootloaderWatch();
    }

    private void OnBaseConnectionChanged(object? sender, EventArgs e) => RefreshStatus();
    private void OnBaseStateReceived(object? sender, BaseState e) => RefreshStatus();

    // ------------------------------------------------------------------------------------------
    // Placa em modo de atualização (DFU/BOOTSEL)
    // ------------------------------------------------------------------------------------------
    //
    // Uma placa em modo de atualização NÃO é o dispositivo normal: ela reenumera como outra coisa
    // (STM32 BOOTLOADER no caso da base), então tudo que detecta conexão comum a dá como ausente. O
    // app dizia "sem conexão" — a única resposta que não ajuda — nos dois momentos em que ela mais
    // parece defeito e não é:
    //
    //   · ANTES de gravar, numa placa de fábrica: ela está pronta, e a tela sugere que deu errado
    //   · DEPOIS de gravar, com o jumper esquecido no modo de atualização: a placa nunca vai rodar o
    //     firmware, e isso é indistinguível de uma gravação que falhou (aconteceu na bancada em
    //     14/08/2026, com a documentação aberta no passo que avisa exatamente isso)
    //
    // A verificação custa um processo (`dfu-util -l`), então roda em intervalo folgado e SÓ enquanto
    // o dispositivo não está conectado normalmente — placa conectada não precisa ser procurada.

    /// <summary>Intervalo da vigília. Injetável só para os testes não esperarem segundos de relógio —
    /// em produção fica nos 3 s do construtor.</summary>
    private readonly TimeSpan _bootloaderPollInterval;
    private readonly IUiDispatcher? _uiDispatcher;
    private CancellationTokenSource? _bootloaderWatch;

    /// <summary>True quando há uma placa em modo de atualização agora (habilita o aviso na tela).</summary>
    [ObservableProperty] private bool _bootloaderDetected;

    /// <summary>Começa (ou refaz) a vigília do bootloader para o dispositivo selecionado.</summary>
    private void RestartBootloaderWatch()
    {
        _bootloaderWatch?.Cancel();
        _bootloaderWatch?.Dispose();
        _bootloaderWatch = null;
        BootloaderDetected = false;

        var device = SelectedDevice;
        if (device is null) return;

        var cts = new CancellationTokenSource();
        _bootloaderWatch = cts;
        _ = WatchBootloaderAsync(device, cts.Token);
    }

    /// <summary>Publica uma mudança da vigília. Passa pelo dispatcher porque a vigília roda FORA da
    /// thread de UI (ver o ConfigureAwait(false) abaixo) e estas propriedades alimentam bindings.</summary>
    private void PublicarDaVigilia(Action mudanca)
    {
        if (_uiDispatcher is not null) _uiDispatcher.Post(mudanca);
        else mudanca();
    }

    // ⚠️ TODOS os awaits desta função usam ConfigureAwait(false), e isso NÃO é estilo: sem ele, cada
    // continuação da vigília volta para o SynchronizationContext capturado na construção. O fluxo de
    // gravação reporta progresso por IProgress<double>, que POSTA nesse mesmo contexto — e o relatório
    // ficava atrás da vigília na fila. O sintoma foi o teste de gravação passando a falhar em
    // `Progress == 1.0` assim que a vigília nasceu: o flash rodava, o valor era reportado, e não
    // chegava a tempo. Uma tarefa de fundo que ocupa o contexto da UI atrasa o que é da UI.
    private async Task WatchBootloaderAsync(IDeviceUpdater device, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Durante a gravação o próprio fluxo já está falando com o bootloader; um segundo
            // `dfu-util -l` no meio disso só disputa o dispositivo.
            if (!IsSending && !DeviceIsConnected(device.Kind))
            {
                bool presente;
                try { presente = await device.IsBootloaderPresentAsync().ConfigureAwait(false); }
                catch { presente = false; }   // sem dfu-util, sem permissão: some o aviso, não quebra a tela

                if (ct.IsCancellationRequested) return;
                if (presente != BootloaderDetected)
                    PublicarDaVigilia(() => { BootloaderDetected = presente; RefreshStatus(); });
            }
            else if (BootloaderDetected)
            {
                PublicarDaVigilia(() => { BootloaderDetected = false; RefreshStatus(); });
            }

            try { await Task.Delay(_bootloaderPollInterval, ct).ConfigureAwait(false); }
            catch (TaskCanceledException) { return; }
        }
    }

    private bool DeviceIsConnected(DeviceKind kind)
    {
        if (_deviceStatus is not null) return _deviceStatus(kind).Item1;
        return _baseSession?.IsConnected == true;
    }

    /// <summary>Atualiza o selo de status para refletir o DISPOSITIVO SELECIONADO (não só a base). Chamado ao
    /// trocar de dispositivo e nos eventos de conexão das sessões (a base internamente; as demais via
    /// CompositionRoot). Sem provider (`deviceStatus` null), cai no comportamento antigo (só a base).</summary>
    public void RefreshStatus()
    {
        var kind = SelectedDevice?.Kind;
        if (kind is null)
        {
            ConnectedDeviceInfo = "Selecione um dispositivo.";
            return;
        }

        if (_deviceStatus is null)
        {
            // Fallback (sem provider): comportamento antigo, só a base.
            if (_baseSession is null || !_baseSession.IsConnected)
            {
                ConnectedDeviceInfo = BootloaderDetected
                    ? $"Placa em modo de atualização ({SelectedDevice?.BootloaderName}) — pronta para gravar."
                    : "Nenhuma placa detectada.";
                return;
            }
            var vb = _baseSession.FirmwareVersion;
            ConnectedDeviceInfo = $"DriveLab Base detectada — firmware v{vb.Major}.{vb.Minor}.{vb.Patch}";
            return;
        }

        var (connected, v) = _deviceStatus(kind.Value);
        var label = DeviceLabel(kind.Value);
        if (!connected)
        {
            // Placa em modo de atualização não está "sem conexão" — está esperando ser gravada.
            ConnectedDeviceInfo = BootloaderDetected
                ? $"{label} em modo de atualização ({SelectedDevice?.BootloaderName}) — pronta para gravar."
                : $"{label}: sem conexão.";
            return;
        }
        bool hasVersion = v.Major != 0 || v.Minor != 0 || v.Patch != 0;
        ConnectedDeviceInfo = hasVersion
            ? $"{label} conectado — firmware v{v.Major}.{v.Minor}.{v.Patch}"
            : $"{label} conectado.";
    }

    private static string DeviceLabel(DeviceKind kind) => kind switch
    {
        DeviceKind.Base => "Base",
        DeviceKind.Pedal => "Pedal",
        DeviceKind.Handbrake => "Freio de mão",
        DeviceKind.Wheel => "Volante",
        _ => kind.ToString(),
    };

    partial void OnSelectedDeviceChanged(IDeviceUpdater? value)
    {
        RevalidateCurrentFile();
        UpdateCheckMessage = "";   // resultado do check é por-dispositivo
        UpdateDownloadable = false;
        _pendingAsset = null;
        RefreshStatus();           // selo reflete o dispositivo agora selecionado
        RestartBootloaderWatch();  // o bootloader procurado é o DESTE dispositivo (DFU vs BOOTSEL)
    }

    /// <summary>Consulta o GitHub e informa a última versão do dispositivo selecionado (e se é mais nova que a
    /// instalada, quando dá pra comparar — hoje só a base tem a versão via telemetria).</summary>
    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (_releaseClient is null || SelectedDevice is null) return;
        var device = SelectedDevice;
        UpdateCheckMessage = "Verificando…";
        UpdateDownloadable = false;
        _pendingAsset = null;
        try
        {
            var releases = await _releaseClient.ListReleasesAsync();
            var prefix = GitHubReleaseClient.TagPrefixFor(device.Kind);
            var latest = GitHubReleaseClient.LatestFor(releases, prefix);
            if (latest is null)
            {
                UpdateCheckMessage = $"Nenhum release publicado para {device.Kind}.";
                return;
            }
            GitHubReleaseClient.TryParseVersion(latest.TagName, prefix, out var v);
            if (device.Kind == DeviceKind.Base && _baseSession is { IsConnected: true })
            {
                var inst = _baseSession.FirmwareVersion;
                UpdateCheckMessage = GitHubReleaseClient.IsNewer(v, inst)
                    ? $"⬆ Nova versão: v{v.Major}.{v.Minor}.{v.Patch} (instalada v{inst.Major}.{inst.Minor}.{inst.Patch})."
                    : $"✓ Está atualizado (v{inst.Major}.{inst.Minor}.{inst.Patch}).";
            }
            else
            {
                UpdateCheckMessage = $"Última no GitHub: v{v.Major}.{v.Minor}.{v.Patch}.";
            }

            // Habilita "Baixar e usar" se o release traz o asset certo (.bin/.uf2) e sabemos baixar.
            _pendingAsset = GitHubReleaseClient.AssetFor(latest, device.Kind);
            UpdateDownloadable = _pendingAsset is not null && _downloadBytes is not null;
        }
        catch (Exception ex)
        {
            UpdateCheckMessage = $"Falha ao verificar: {ex.Message}";
        }
    }

    /// <summary>Baixa o asset do release para um arquivo temporário e o carrega no fluxo de flash (valida na hora;
    /// depois é só clicar Enviar). Não flasheia sozinho — o usuário confirma no botão Enviar.</summary>
    [RelayCommand(CanExecute = nameof(CanDownloadUpdate))]
    private async Task DownloadUpdateAsync()
    {
        if (_downloadBytes is null || _pendingAsset is null) return;
        UpdateCheckMessage = "Baixando…";
        try
        {
            var bytes = await _downloadBytes(new Uri(_pendingAsset.DownloadUrl));
            var dest = Path.Combine(Path.GetTempPath(), _pendingAsset.Name);
            await File.WriteAllBytesAsync(dest, bytes);
            FirmwarePath = dest;
            await ValidateAsync(dest);
            UpdateCheckMessage = $"Baixado: {_pendingAsset.Name} — confira e clique Enviar.";
        }
        catch (Exception ex)
        {
            UpdateCheckMessage = $"Falha ao baixar: {ex.Message}";
        }
    }

    private bool CanDownloadUpdate() => _downloadBytes is not null && _pendingAsset is not null && !IsSending;

    [RelayCommand]
    private async Task SelectFile()
    {
        var path = await _filePicker.PickFirmwareFileAsync();
        if (path is null)
            return;

        FirmwarePath = path;
        await ValidateAsync(path);
    }

    private void RevalidateCurrentFile()
    {
        if (string.IsNullOrEmpty(FirmwarePath))
            return;
        _ = ValidateAsync(FirmwarePath);
    }

    private async Task ValidateAsync(string path)
    {
        IsFirmwareValid = false;
        try
        {
            var bytes = await _readFile(path);
            if (SelectedDevice is null)
            {
                ValidationMessage = "Selecione um dispositivo antes de validar o arquivo.";
                return;
            }

            if (SelectedDevice.ValidateFirmware(bytes, out var error))
            {
                var info = FirmwareFile.Read(bytes);
                ValidationMessage = $"✓ Firmware válido para {SelectedDevice.Kind} — versão {info.Version}.";
                IsFirmwareValid = true;
            }
            else
            {
                ValidationMessage = $"✗ {error}";
            }
        }
        catch (Exception ex)
        {
            ValidationMessage = $"✗ Não foi possível ler o arquivo: {ex.Message}";
        }
    }

    private bool CanSend() => IsFirmwareValid && SelectedDevice is not null && !IsSending && !NeedsManualDfu;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        if (SelectedDevice is null || !IsFirmwareValid)
            return;

        var device = SelectedDevice;
        _inFlightDevice = device;
        IsSending = true;
        NeedsManualDfu = false;
        Progress = 0;
        try
        {
            StatusMessage = "Enviando comando para entrar em modo de atualização (DFU)...";
            await device.EnterBootloaderAsync();

            // Controle exclusivo da USB: pausa o auto-connect e solta o handle HID, para o
            // dispositivo re-enumerar como DFU sem outro ator reabrir o device (ver
            // IDeviceAccessCoordinator). Chamado DEPOIS do EnterDfu, que ainda usa o transporte.
            if (_coordinator is not null)
            {
                // Marca ANTES do await: se BeginExclusiveAsync pausar o auto-connect e então lançar,
                // o ReleaseExclusiveAsync (no catch) ainda retoma — nunca deixa o poller pausado.
                _exclusiveHeld = true;
                await _coordinator.BeginExclusiveAsync(device.Kind);
            }

            StatusMessage = $"Aguardando o bootloader ({device.BootloaderName}) — salto automático...";
            if (await device.WaitForBootloaderAsync(AutoBootloaderTimeout))
            {
                await FlashAndReportAsync(device);
                await ReleaseExclusiveAsync();
            }
            else
            {
                // O salto por software não subiu o bootloader. Cai pro gatilho manual: mantém o acesso
                // exclusivo retido e mostra a instrução CERTA do dispositivo (SW1→DFU na base; BOOTSEL no RP2040).
                EnterManualMode(device);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha na atualização: {ex.Message}";
            await ReleaseExclusiveAsync();
        }
        finally
        {
            IsSending = false;
        }
    }

    private bool CanContinueOrCancel() => NeedsManualDfu && !IsSending;

    /// <summary>Etapa manual: o usuário pôs SW1→DFU e reiniciou a placa; detecta o bootloader e grava.</summary>
    [RelayCommand(CanExecute = nameof(CanContinueOrCancel))]
    private async Task ContinueDfu()
    {
        var device = _inFlightDevice;
        if (device is null)
            return;

        IsSending = true;
        try
        {
            StatusMessage = $"Procurando o bootloader ({device.BootloaderName})...";
            if (await device.WaitForBootloaderAsync(ManualBootloaderTimeout))
            {
                NeedsManualDfu = false;
                await FlashAndReportAsync(device);
                await ReleaseExclusiveAsync();
            }
            else
            {
                // Continua na etapa manual — deixa o usuário conferir e tentar de novo (texto por dispositivo).
                StatusMessage = device.Kind == DeviceKind.Base
                    ? "Ainda não vejo o bootloader (0483:df11). Confirme SW1 em DFU + power-cycle e clique em Continuar de novo."
                    : "Ainda não vejo o volume RPI-RP2. Confirme o BOOTSEL e clique em Continuar de novo.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha na atualização: {ex.Message}";
            NeedsManualDfu = false;
            await ReleaseExclusiveAsync();
        }
        finally
        {
            IsSending = false;
        }
    }

    /// <summary>Aborta a etapa manual: solta o acesso exclusivo (retoma o auto-connect) e reseta o estado.</summary>
    [RelayCommand(CanExecute = nameof(CanContinueOrCancel))]
    private async Task CancelDfu()
    {
        StatusMessage = "Atualização cancelada.";
        NeedsManualDfu = false;
        await ReleaseExclusiveAsync();
    }

    /// <summary>Entra na etapa manual com a instrução CERTA do dispositivo: base = SW1→DFU (STM32);
    /// RP2040 (pedal/freio/aro) = BOOTSEL (RPI-RP2).</summary>
    private void EnterManualMode(IDeviceUpdater device)
    {
        NeedsManualDfu = true;
        if (device.Kind == DeviceKind.Base)
        {
            StatusMessage = "A placa não entrou em DFU sozinha. Coloque a chave SW1 em DFU, faça um power-cycle (RESET/energia) e clique em Continuar.";
            ManualInstructions = "1) Coloque a chave SW1 da placa em DFU.\n2) Faça um power-cycle (RESET ou tire/recoloque a energia).\n3) Clique em Continuar.";
        }
        else
        {
            StatusMessage = "O dispositivo não entrou em BOOTSEL sozinho (firmware antigo, sem o comando?). Coloque-o em BOOTSEL e clique em Continuar.";
            ManualInstructions = "1) Segure o botão BOOT da placa e conecte o USB (ou, já plugado: segure BOOT, aperte e solte RESET, solte BOOT).\n2) O volume RPI-RP2 deve aparecer no Finder.\n3) Clique em Continuar.";
        }
    }

    private async Task FlashAndReportAsync(IDeviceUpdater device)
    {
        StatusMessage = "Enviando firmware...";
        // MONOTÔNICA de propósito: só aceita valor MAIOR. Progress<T> entrega os avisos de forma
        // assíncrona, então um relatório de 50% emitido antes do fim pode chegar DEPOIS de a barra
        // fechar em 100% logo abaixo — e ela volta para a metade com o texto já dizendo "concluída
        // com sucesso". Barra que anda para trás é a aparência exata de uma gravação que deu errado
        // no fim, no momento em que a pessoa mais precisa confiar no que vê.
        var progress = new Progress<double>(p => { if (p > Progress) Progress = p; });
        await device.FlashAsync(FirmwarePath, progress);

        // 100% é uma CONCLUSÃO, não o último relatório. Progress<T> entrega os avisos de forma
        // assíncrona (posta no contexto/pool), então o valor final pode chegar depois daqui — e se o
        // dfu-util simplesmente não emitir a linha de 100%, nunca chega. Nos dois casos a barra
        // ficaria parada perto do fim com a gravação já concluída, que é a aparência exata de um
        // travamento. Gravou sem erro: a barra fecha.
        Progress = 1.0;
        StatusMessage = "Atualização concluída com sucesso.";
    }

    /// <summary>Retoma o auto-connect (reconecta a placa já com o firmware novo) e limpa o estado em voo.
    /// Idempotente — seguro chamar mesmo sem acesso exclusivo retido.</summary>
    private async Task ReleaseExclusiveAsync()
    {
        if (_exclusiveHeld && _coordinator is not null && _inFlightDevice is not null)
            await _coordinator.EndExclusiveAsync(_inFlightDevice.Kind);
        _exclusiveHeld = false;
        _inFlightDevice = null;
    }

    public override void Dispose()
    {
        // A vigília do bootloader roda um processo a cada 3 s; deixá-la viva depois da tela morrer
        // seria um `dfu-util -l` perpétuo em segundo plano.
        _bootloaderWatch?.Cancel();
        _bootloaderWatch?.Dispose();
        _bootloaderWatch = null;

        if (_baseSession is not null)
        {
            _baseSession.Connected -= OnBaseConnectionChanged;
            _baseSession.Disconnected -= OnBaseConnectionChanged;
            _baseSession.StateReceived -= OnBaseStateReceived;
        }
        base.Dispose();
    }
}
