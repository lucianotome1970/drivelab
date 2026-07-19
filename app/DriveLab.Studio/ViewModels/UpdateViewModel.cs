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
using DriveLab.Core.Update;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>VM do módulo "Atualizar firmware". Recebe a lista de <see cref="IDeviceUpdater"/>
/// disponíveis (hoje só a base) já ligados aos transportes reais; o seletor de arquivo e a
/// leitura de bytes do disco são injetados para manter <see cref="SelectFileAsync"/> e
/// <see cref="SendAsync"/> testáveis sem tocar IO/UI real.</summary>
public sealed partial class UpdateViewModel : ViewModelBase
{
    private static readonly TimeSpan BootloaderTimeout = TimeSpan.FromSeconds(15);

    private readonly IFilePicker _filePicker;
    private readonly Func<string, Task<byte[]>> _readFile;
    private readonly IDeviceAccessCoordinator? _coordinator;

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

    [ObservableProperty] private double _progress;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private bool _isSending;

    public UpdateViewModel(IReadOnlyList<IDeviceUpdater> devices, IFilePicker? filePicker = null,
        Func<string, Task<byte[]>>? readFile = null, IDeviceAccessCoordinator? coordinator = null)
    {
        Devices = devices;
        _filePicker = filePicker ?? new AvaloniaFilePicker();
        _readFile = readFile ?? (path => File.ReadAllBytesAsync(path));
        _coordinator = coordinator;
        _selectedDevice = devices.Count > 0 ? devices[0] : null;
    }

    partial void OnSelectedDeviceChanged(IDeviceUpdater? value) => RevalidateCurrentFile();

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

    private bool CanSend() => IsFirmwareValid && SelectedDevice is not null && !IsSending;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        if (SelectedDevice is null || !IsFirmwareValid)
            return;

        var device = SelectedDevice;
        IsSending = true;
        Progress = 0;
        try
        {
            StatusMessage = "Enviando comando para entrar em modo de atualização (DFU)...";
            await device.EnterBootloaderAsync();

            // Controle exclusivo da USB: pausa o auto-connect e solta o handle HID, para o
            // dispositivo re-enumerar como DFU sem outro ator reabrir o device (ver
            // IDeviceAccessCoordinator). Chamado DEPOIS do EnterDfu, que ainda usa o transporte.
            if (_coordinator is not null)
                await _coordinator.BeginExclusiveAsync(device.Kind);

            StatusMessage = $"Aguardando o bootloader ({device.BootloaderName})...";
            var found = await device.WaitForBootloaderAsync(BootloaderTimeout);
            if (!found)
            {
                StatusMessage = "O dispositivo não reapareceu em modo de atualização. Verifique o cabo USB e tente novamente.";
                return;
            }

            StatusMessage = "Enviando firmware...";
            var progress = new Progress<double>(p => Progress = p);
            await device.FlashAsync(FirmwarePath, progress);

            StatusMessage = "Atualização concluída com sucesso.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Falha na atualização: {ex.Message}";
        }
        finally
        {
            // Retoma o auto-connect (reconecta a base já com o firmware novo). Idempotente mesmo
            // que BeginExclusiveAsync não tenha rodado (ex.: EnterDfu falhou antes).
            if (_coordinator is not null)
                await _coordinator.EndExclusiveAsync(device.Kind);
            IsSending = false;
        }
    }
}
