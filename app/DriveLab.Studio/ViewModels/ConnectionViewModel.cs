// ============================================================================
//  DriveLab
//  ConnectionViewModel.cs — VM do estado de conexão da base, reagindo a eventos manuais e de auto-conexão.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Studio.Services;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.ViewModels;

/// <summary>Estado da conexão da base no topo. Reage aos eventos da <see cref="DeviceSession"/>
/// (marshalados p/ a UI), então tanto o clique manual (simulador) quanto a conexão automática
/// (<see cref="DeviceAutoConnector"/>, modo real) atualizam o status.</summary>
public partial class ConnectionViewModel : ViewModelBase
{
    private readonly DeviceSession _session;
    private readonly IUiDispatcher _dispatcher;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = L.Get("Status_Disconnected");

    public ConnectionViewModel(DeviceSession session, IUiDispatcher dispatcher)
    {
        _session = session;
        _dispatcher = dispatcher;
        _session.Connected += OnConnectionChanged;
        _session.Disconnected += OnConnectionChanged;
        _isConnected = _session.IsConnected;
        _statusText = _isConnected ? L.Get("Status_Connected") : L.Get("Status_Disconnected");
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => _dispatcher.Post(Refresh);

    private void Refresh()
    {
        IsConnected = _session.IsConnected;
        StatusText = IsConnected ? L.Get("Status_Connected") : L.Get("Status_Disconnected");
    }

    // Comandos manuais — usados só no modo simulador (no modo real a conexão é automática).
    [RelayCommand]
    private Task ConnectAsync() => _session.ConnectAsync();

    [RelayCommand]
    private Task DisconnectAsync() => _session.DisconnectAsync();

    public override void Dispose()
    {
        _session.Connected -= OnConnectionChanged;
        _session.Disconnected -= OnConnectionChanged;
        base.Dispose();
    }
}
