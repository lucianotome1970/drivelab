// ============================================================================
//  DriveLab
//  PedalDeviceSession.cs — Fachada sobre um IPedalTransport que marshala telemetria dos pedais para a thread de UI.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Studio.Services;

/// <summary>Fachada sobre um <see cref="IPedalTransport"/>, marshalando telemetria p/ a thread UI.</summary>
public sealed class PedalDeviceSession : IDisposable
{
    private readonly IPedalTransport _transport;
    private readonly IUiDispatcher _dispatcher;

    public PedalDeviceSession(IPedalTransport transport, IUiDispatcher dispatcher, string sourceLabel = "Simulador")
    {
        _transport = transport;
        _dispatcher = dispatcher;
        SourceLabel = sourceLabel;
        _transport.StateReceived += OnTransportState;
    }

    public event EventHandler<PedalState>? StateReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<PedalSettingChangedEventArgs>? SettingChanged;

    public bool IsConnected => _transport.IsConnected;
    public bool SupportsConfig => _transport.SupportsConfig;
    public string SourceLabel { get; }
    public FirmwareVersion FirmwareVersion => _transport.FirmwareVersion;

    public async Task ConnectAsync()
    {
        // O transporte cuida do seu próprio streaming/leitura no ConnectAsync.
        await _transport.ConnectAsync();
        Connected?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync()
    {
        await _transport.DisconnectAsync();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async Task WriteSettingAsync(PedalSettingId id, PedalIndex pedal, SettingValue value)
    {
        await _transport.WriteSettingAsync(id, pedal, value);
        _dispatcher.Post(() => SettingChanged?.Invoke(this, new PedalSettingChangedEventArgs(id, pedal, value)));
    }

    public Task<SettingValue> ReadSettingAsync(PedalSettingId id, PedalIndex pedal) =>
        _transport.ReadSettingAsync(id, pedal);

    public Task SendCommandAsync(PedalCommandId command, byte arg = 0) =>
        _transport.SendCommandAsync(command, arg);

    private void OnTransportState(object? sender, PedalState state) =>
        _dispatcher.Post(() => StateReceived?.Invoke(this, state));

    public void Dispose()
    {
        _transport.StateReceived -= OnTransportState;
        _ = _transport.DisconnectAsync(); // para timer/leitura do transporte
    }
}

public sealed class PedalSettingChangedEventArgs : EventArgs
{
    public PedalSettingChangedEventArgs(PedalSettingId id, PedalIndex pedal, SettingValue value)
    {
        Id = id;
        Pedal = pedal;
        Value = value;
    }

    public PedalSettingId Id { get; }
    public PedalIndex Pedal { get; }
    public SettingValue Value { get; }
}
