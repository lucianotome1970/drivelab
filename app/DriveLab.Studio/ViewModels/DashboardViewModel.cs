// ============================================================================
//  DriveLab
//  DashboardViewModel.cs — VM do dashboard do volante: centralizar, ajustar ângulo máximo e status de conexão.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

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
    private readonly DeviceSession _session;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CenterCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetMaxAngleCommand))]
    private bool _isConnected;

    [ObservableProperty]
    private double _angleDegrees;

    [ObservableProperty]
    private double _positionPercent;

    [ObservableProperty]
    private int _motionRange = 900;

    public DashboardViewModel(DeviceSession session)
    {
        _session = session;
        _session.StateReceived += OnState;
        _session.Connected += OnConnected;
        _session.Disconnected += OnDisconnected;
        _session.SettingChanged += OnSettingChanged;
        IsConnected = _session.IsConnected;
    }

    public override void Dispose()
    {
        _session.StateReceived -= OnState;
        _session.Connected -= OnConnected;
        _session.Disconnected -= OnDisconnected;
        _session.SettingChanged -= OnSettingChanged;
        base.Dispose();
    }

    private async void OnConnected(object? sender, EventArgs e)
    {
        IsConnected = true;
        try
        {
            // Mostra o valor real do dispositivo (não o default do VM).
            var value = await _session.ReadSettingAsync(SettingId.MotionRange);
            MotionRange = (int)value.AsDouble;
        }
        catch
        {
            // Leitura pode falhar/expirar (ex.: dispositivo sumiu); não derruba o app.
        }
    }

    private void OnDisconnected(object? sender, EventArgs e) => IsConnected = false;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Id == SettingId.MotionRange)
            MotionRange = (int)e.Value.AsDouble;
    }

    private void OnState(object? sender, DeviceState state)
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
        return _session.SendCommandAsync(DeviceCommand.ResetCenter);
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private async Task SetMaxAngleAsync(string degrees)
    {
        if (!_session.IsConnected)
            return;
        var value = int.Parse(degrees, CultureInfo.InvariantCulture);
        await _session.WriteSettingAsync(SettingId.MotionRange, new SettingValue(SettingType.UInt16, value));
        MotionRange = value;
    }
}
