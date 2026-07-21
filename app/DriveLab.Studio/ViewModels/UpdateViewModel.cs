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

    public UpdateViewModel(IReadOnlyList<IDeviceUpdater> devices, IFilePicker? filePicker = null,
        Func<string, Task<byte[]>>? readFile = null, IDeviceAccessCoordinator? coordinator = null,
        BaseSession? baseSession = null, GitHubReleaseClient? releaseClient = null)
    {
        Devices = devices;
        _filePicker = filePicker ?? new AvaloniaFilePicker();
        _readFile = readFile ?? (path => File.ReadAllBytesAsync(path));
        _coordinator = coordinator;
        _baseSession = baseSession;
        _releaseClient = releaseClient;
        _selectedDevice = devices.Count > 0 ? devices[0] : null;

        if (_baseSession is not null)
        {
            // A base conecta/desconecta sozinha (auto-connect) e a versão chega na 1ª telemetria —
            // por isso escutamos os 3 eventos e recalculamos o rótulo a cada um.
            _baseSession.Connected += OnBaseConnectionChanged;
            _baseSession.Disconnected += OnBaseConnectionChanged;
            _baseSession.StateReceived += OnBaseStateReceived;
            RefreshConnectedInfo();
        }
    }

    private void OnBaseConnectionChanged(object? sender, EventArgs e) => RefreshConnectedInfo();
    private void OnBaseStateReceived(object? sender, BaseState e) => RefreshConnectedInfo();

    private void RefreshConnectedInfo()
    {
        if (_baseSession is null || !_baseSession.IsConnected)
        {
            ConnectedDeviceInfo = "Nenhuma placa detectada.";
            return;
        }
        var v = _baseSession.FirmwareVersion;
        ConnectedDeviceInfo = $"DriveLab Base detectada — firmware v{v.Major}.{v.Minor}.{v.Patch}";
    }

    partial void OnSelectedDeviceChanged(IDeviceUpdater? value)
    {
        RevalidateCurrentFile();
        UpdateCheckMessage = "";   // resultado do check é por-dispositivo
    }

    /// <summary>Consulta o GitHub e informa a última versão do dispositivo selecionado (e se é mais nova que a
    /// instalada, quando dá pra comparar — hoje só a base tem a versão via telemetria).</summary>
    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (_releaseClient is null || SelectedDevice is null) return;
        var device = SelectedDevice;
        UpdateCheckMessage = "Verificando…";
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
        }
        catch (Exception ex)
        {
            UpdateCheckMessage = $"Falha ao verificar: {ex.Message}";
        }
    }

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
                // O salto por software não subiu o bootloader (conhecido nesta placa). Cai pro
                // gatilho manual: mantém o acesso exclusivo retido e pede SW1→DFU + power-cycle.
                NeedsManualDfu = true;
                StatusMessage = "A placa não entrou em DFU sozinha. Coloque a chave SW1 em DFU, faça um power-cycle (RESET/energia) e clique em Continuar.";
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
                // Continua na etapa manual — deixa o usuário conferir a chave/reset e tentar de novo.
                StatusMessage = "Ainda não vejo o bootloader (0483:df11). Confirme SW1 em DFU + power-cycle e clique em Continuar de novo.";
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

    private async Task FlashAndReportAsync(IDeviceUpdater device)
    {
        StatusMessage = "Enviando firmware...";
        var progress = new Progress<double>(p => Progress = p);
        await device.FlashAsync(FirmwarePath, progress);
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
}
