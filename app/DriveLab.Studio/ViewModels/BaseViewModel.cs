// ============================================================================
//  DriveLab
//  BaseViewModel.cs — VM do card da base no dashboard: controle único de força total (TotalStrength).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Card da base no dashboard: um único controle de força total
/// (<see cref="BaseSettingId.TotalStrength"/>). Envolve o MESMO <see cref="DeviceSession"/> do
/// volante — lê o valor ao conectar e grava ao alterar (espelha o padrão do
/// <see cref="DashboardViewModel"/> p/ MotionRange). Não descarta a sessão (compartilhada).</summary>
public partial class BaseViewModel : ViewModelBase
{
    private readonly DeviceSession _session;
    private bool _loading;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int _totalStrength = 100;

    public BaseViewModel(DeviceSession session)
    {
        _session = session;
        _session.Connected += OnConnected;
        _session.Disconnected += OnDisconnected;
        _session.SettingChanged += OnSettingChanged;
        IsConnected = _session.IsConnected;
    }

    private async void OnConnected(object? sender, EventArgs e)
    {
        IsConnected = true;
        try
        {
            // Mostra o valor real do dispositivo (não o default do VM).
            _loading = true;
            var value = await _session.ReadSettingAsync(BaseSettingId.TotalStrength);
            TotalStrength = (int)value.AsDouble;
        }
        catch
        {
            // Leitura pode falhar/expirar (ex.: dispositivo sumiu); não derruba o app.
        }
        finally
        {
            _loading = false;
        }
    }

    private void OnDisconnected(object? sender, EventArgs e) => IsConnected = false;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Id != BaseSettingId.TotalStrength)
            return;
        _loading = true;
        TotalStrength = (int)e.Value.AsDouble;
        _loading = false;
    }

    partial void OnTotalStrengthChanged(int value)
    {
        if (_loading || !_session.IsConnected)
            return;
        var d = BaseSettingsSchema.Get(BaseSettingId.TotalStrength);
        var clamped = (int)Math.Clamp(value, d.Min, d.Max);
        _ = _session.WriteSettingAsync(BaseSettingId.TotalStrength, new SettingValue(d.Type, clamped));
    }

    public override void Dispose()
    {
        _session.Connected -= OnConnected;
        _session.Disconnected -= OnDisconnected;
        _session.SettingChanged -= OnSettingChanged;
        base.Dispose();
    }
}
