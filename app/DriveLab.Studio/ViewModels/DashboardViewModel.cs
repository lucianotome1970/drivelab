// ============================================================================
//  DriveLab
//  DashboardViewModel.cs — VM do dashboard do volante: centralizar, ajustar ângulo máximo e status de conexão.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly BaseSession _session;
    private readonly WheelDeviceSession? _rim;
    private readonly CenterHotkeyController? _centerHotkey;

    /// <summary>true enquanto o usuário está atribuindo o atalho: o próximo gesto (HID/teclado) é capturado.</summary>
    [ObservableProperty] private bool _isAssigningCenterButton;

    /// <summary>Mapeamentos ativos do atalho (estilo ACC: vários ao mesmo tempo, cada um com seu ×).</summary>
    public ObservableCollection<CenterBindingRowViewModel> CenterBindings { get; } = new();

    /// <summary>true quando não há nenhum mapeamento (a UI mostra um "—" / dica).</summary>
    public bool HasNoCenterBindings => CenterBindings.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CenterCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetMaxAngleCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveToControllerCommand))]
    private bool _isConnected;

    /// <summary>Há mudança feita no dashboard que ainda não foi gravada na flash da base (mesmo padrão
    /// do HandbrakeViewModel). O botão "Salvar no controlador" só acende com isto — assim o usuário vê
    /// que falta gravar, e não fica clicando à toa quando app e flash já estão iguais.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveToControllerCommand))]
    private bool _isDirty;

    /// <summary>Conexão do ARO removível (rim). O card "Volante" do dash acende por esta —
    /// é o dispositivo que o usuário pluga. O ângulo/Center vêm da base (<see cref="IsConnected"/>).</summary>
    [ObservableProperty]
    private bool _isWheelConnected;

    [ObservableProperty]
    private double _angleDegrees;

    [ObservableProperty]
    private double _positionPercent;

    [ObservableProperty]
    private int _motionRange = 900;

    public DashboardViewModel(BaseSession session, WheelDeviceSession? rim = null,
                              CenterHotkeyController? centerHotkey = null)
    {
        _session = session;
        _rim = rim;
        _centerHotkey = centerHotkey;
        _session.StateReceived += OnState;
        _session.Connected += OnConnected;
        _session.Disconnected += OnDisconnected;
        _session.SettingChanged += OnSettingChanged;
        IsConnected = _session.IsConnected;

        if (_rim is not null)
        {
            _rim.Connected += OnRimChanged;
            _rim.Disconnected += OnRimChanged;
            _isWheelConnected = _rim.IsConnected;
        }

        // Atalho de centralizar (HID/teclado): o controlador junta as fontes, aprende os gestos e dispara
        // o ResetCenter. A VM reflete a LISTA de mapeamentos e o estado de captura na UI.
        if (_centerHotkey is not null)
        {
            _centerHotkey.Changed += OnCenterHotkeyChanged;
            IsAssigningCenterButton = _centerHotkey.IsAssigning;
            RebuildCenterBindings();
        }
    }

    private void OnRimChanged(object? sender, EventArgs e) => IsWheelConnected = _rim?.IsConnected ?? false;

    private void OnCenterHotkeyChanged(object? sender, EventArgs e)
    {
        IsAssigningCenterButton = _centerHotkey!.IsAssigning;
        RebuildCenterBindings();
    }

    private void RebuildCenterBindings()
    {
        CenterBindings.Clear();
        if (_centerHotkey is not null)
            foreach (var b in _centerHotkey.Bindings)
                CenterBindings.Add(new CenterBindingRowViewModel(b, () => _centerHotkey.Remove(b)));
        OnPropertyChanged(nameof(HasNoCenterBindings));
    }

    /// <summary>Inicia/cancela a captura de um NOVO atalho (o próximo gesto vira mais um mapeamento).</summary>
    [RelayCommand]
    private void ToggleAssignCenterButton() => _centerHotkey?.ToggleAssign();

    /// <summary>Remove TODOS os mapeamentos do atalho de centralizar.</summary>
    [RelayCommand]
    private void ClearAllCenterButtons() => _centerHotkey?.ClearAll();

    /// <summary>Persiste na flash da base o que está no dashboard (ângulo de giro / centro).
    /// O botão "Salvar no controlador" das abas de config não alcança o dashboard: o
    /// <c>MotionRange</c> vive AQUI, então sem isto o ângulo escolhido se perde no power-cycle
    /// (constatado na bancada em 2026-08-05). Mesmo comando do firmware (<c>CMD_SAVE</c>): grava o
    /// blob de settings na FFB_NVM com o motor em IDLE — a força cai ~1 s e o motor re-arma.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveToControllerAsync()
    {
        await _session.SendCommandAsync(BaseCommand.SaveSettings);
        IsDirty = false;   // gravou na flash: app == firmware
    }

    private bool CanSave() => IsConnected && IsDirty;

    public override void Dispose()
    {
        _session.StateReceived -= OnState;
        _session.Connected -= OnConnected;
        _session.Disconnected -= OnDisconnected;
        _session.SettingChanged -= OnSettingChanged;
        if (_rim is not null)
        {
            _rim.Connected -= OnRimChanged;
            _rim.Disconnected -= OnRimChanged;
        }
        if (_centerHotkey is not null)
        {
            _centerHotkey.Changed -= OnCenterHotkeyChanged;
            _centerHotkey.Dispose();   // solta o hook global de teclado e os handles HID
        }
        base.Dispose();
    }

    private async void OnConnected(object? sender, EventArgs e)
    {
        IsConnected = true;
        try
        {
            // Mostra o valor real do dispositivo (não o default do VM).
            var value = await _session.ReadSettingAsync(BaseSettingId.MotionRange);
            MotionRange = (int)value.AsDouble;
            IsDirty = false;   // acabou de ler da base: app == flash
        }
        catch
        {
            // Leitura pode falhar/expirar (ex.: dispositivo sumiu); não derruba o app.
        }
    }

    private void OnDisconnected(object? sender, EventArgs e) => IsConnected = false;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Id == BaseSettingId.MotionRange)
            MotionRange = (int)e.Value.AsDouble;
    }

    private void OnState(object? sender, BaseState state)
    {
        AngleDegrees = state.AngleDeciDeg / 10.0;
        PositionPercent = state.Position / 100.0;
        IsConnected = _session.IsConnected;
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task CenterAsync()
    {
        if (!_session.IsConnected)
            return Task.CompletedTask;
        return _session.SendCommandAsync(BaseCommand.ResetCenter);
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task SetMaxAngleAsync(string degrees)
    {
        if (!_session.IsConnected)
            return;
        var value = int.Parse(degrees, CultureInfo.InvariantCulture);
        await _session.WriteSettingAsync(BaseSettingId.MotionRange, new SettingValue(SettingType.UInt16, value));
        MotionRange = value;
        // O write vai só para a RAM do firmware (vale na hora, some no power-cycle). Marca pendente
        // para o "Salvar no controlador" acender e o usuário saber que falta gravar na flash.
        IsDirty = true;
    }
}
