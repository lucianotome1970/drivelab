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
/// (<see cref="BaseSettingId.TotalStrength"/>). Envolve o MESMO <see cref="BaseSession"/> do
/// volante — lê o valor ao conectar e grava ao alterar (espelha o padrão do
/// <see cref="DashboardViewModel"/> p/ MotionRange). Não descarta a sessão (compartilhada).</summary>
public partial class BaseViewModel : ViewModelBase, IPendingWrite
{
    private readonly BaseSession _session;
    private bool _loading;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int _totalStrength = 100;

    public BaseViewModel(BaseSession session)
    {
        _session = session;
        _session.Connected += OnConnected;
        _session.Disconnected += OnDisconnected;
        _session.SettingChanged += OnSettingChanged;
        IsConnected = _session.IsConnected;
        // Já conectado ao criar o card → o Connected não dispara mais; lê a força da placa agora.
        if (IsConnected)
            OnConnected(this, EventArgs.Empty);
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

    /// <summary>Alterado na tela e ainda NÃO enviado à base.</summary>
    public bool IsModified { get; private set; }

    /// <summary>O usuário mexeu na força (não dispara em load/eco do device).</summary>
    public event EventHandler? Edited;

    /// <summary>Envia a força para a base. Chamado SÓ pelo "Salvar no controlador" do dashboard.</summary>
    public async Task PushAsync()
    {
        if (!_session.IsConnected)
            return;
        var d = BaseSettingsSchema.Get(BaseSettingId.TotalStrength);
        var clamped = (int)Math.Clamp(TotalStrength, d.Min, d.Max);
        await _session.WriteSettingAsync(BaseSettingId.TotalStrength, new SettingValue(d.Type, clamped));
        IsModified = false;   // já está na base
    }

    partial void OnTotalStrengthChanged(int value)
    {
        // NÃO envia para a base aqui. Arrastar o controle reconfigurava a base ao vivo, com o motor
        // possivelmente ARMADO, e a cada pixel do slider. Quem envia é o "Salvar no controlador".
        if (_loading || !_session.IsConnected)
            return;
        IsModified = true;
        Edited?.Invoke(this, EventArgs.Empty);
    }

    public override void Dispose()
    {
        _session.Connected -= OnConnected;
        _session.Disconnected -= OnDisconnected;
        _session.SettingChanged -= OnSettingChanged;
        base.Dispose();
    }
}
