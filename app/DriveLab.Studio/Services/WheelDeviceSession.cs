// ============================================================================
//  DriveLab
//  WheelDeviceSession.cs — Fachada sobre IWheelTransport que marshala telemetria do volante p/ a thread de UI.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Studio.Services;

/// <summary>Fachada sobre um <see cref="IWheelTransport"/>, marshalando telemetria p/ a thread UI.
/// Espelha <see cref="HandbrakeDeviceSession"/>, com o extra de enviar cores de LED ao aro.</summary>
public sealed class WheelDeviceSession : IDisposable
{
    private readonly IWheelTransport _transport;
    private readonly IUiDispatcher _dispatcher;

    public WheelDeviceSession(IWheelTransport transport, IUiDispatcher dispatcher, string sourceLabel = "Simulador")
    {
        _transport = transport;
        _dispatcher = dispatcher;
        SourceLabel = sourceLabel;
        _transport.StateReceived += OnTransportState;
    }

    public event EventHandler<WheelState>? StateReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<WheelSettingChangedEventArgs>? SettingChanged;

    public bool IsConnected => _transport.IsConnected;
    public bool SupportsConfig => _transport.SupportsConfig;
    public string SourceLabel { get; }
    public FirmwareVersion FirmwareVersion => _transport.FirmwareVersion;

    public async Task ConnectAsync()
    {
        await _transport.ConnectAsync();
        Connected?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync()
    {
        await _transport.DisconnectAsync();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async Task WriteSettingAsync(WheelSettingId id, SettingValue value)
    {
        await _transport.WriteSettingAsync(id, value);
        _dispatcher.Post(() => SettingChanged?.Invoke(this, new WheelSettingChangedEventArgs(id, value)));
    }

    public Task<SettingValue> ReadSettingAsync(WheelSettingId id) =>
        _transport.ReadSettingAsync(id);

    public Task SendCommandAsync(WheelCommandId command, byte arg = 0) =>
        _transport.SendCommandAsync(command, arg);

    public Task SendLedAsync(WheelLedReport led) =>
        _transport.SendLedAsync(led);

    private void OnTransportState(object? sender, WheelState state) =>
        _dispatcher.Post(() => StateReceived?.Invoke(this, state));

    public void Dispose()
    {
        _transport.StateReceived -= OnTransportState;
        _ = _transport.DisconnectAsync();
    }
}

public sealed class WheelSettingChangedEventArgs : EventArgs
{
    public WheelSettingChangedEventArgs(WheelSettingId id, SettingValue value)
    {
        Id = id;
        Value = value;
    }

    public WheelSettingId Id { get; }
    public SettingValue Value { get; }
}
