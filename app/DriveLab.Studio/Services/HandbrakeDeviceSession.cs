// ============================================================================
//  DriveLab
//  HandbrakeDeviceSession.cs — Fachada sobre IHandbrakeTransport que marshala telemetria do freio de mão p/ a thread de UI.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Studio.Services;

/// <summary>Fachada sobre um <see cref="IHandbrakeTransport"/>, marshalando telemetria p/ a thread UI.</summary>
public sealed class HandbrakeDeviceSession : IDisposable
{
    private readonly IHandbrakeTransport _transport;
    private readonly IUiDispatcher _dispatcher;

    public HandbrakeDeviceSession(IHandbrakeTransport transport, IUiDispatcher dispatcher, string sourceLabel = "Simulador")
    {
        _transport = transport;
        _dispatcher = dispatcher;
        SourceLabel = sourceLabel;
        _transport.StateReceived += OnTransportState;
    }

    public event EventHandler<PedalState>? StateReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<HandbrakeSettingChangedEventArgs>? SettingChanged;

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

    public async Task WriteSettingAsync(HandbrakeSettingId id, SettingValue value)
    {
        await _transport.WriteSettingAsync(id, value);
        _dispatcher.Post(() => SettingChanged?.Invoke(this, new HandbrakeSettingChangedEventArgs(id, value)));
    }

    public Task<SettingValue> ReadSettingAsync(HandbrakeSettingId id) =>
        _transport.ReadSettingAsync(id);

    public Task SendCommandAsync(PedalCommandId command, byte arg = 0) =>
        _transport.SendCommandAsync(command, arg);

    private void OnTransportState(object? sender, PedalState state) =>
        _dispatcher.Post(() => StateReceived?.Invoke(this, state));

    public void Dispose()
    {
        _transport.StateReceived -= OnTransportState;
        _ = _transport.DisconnectAsync();
    }
}

public sealed class HandbrakeSettingChangedEventArgs : EventArgs
{
    public HandbrakeSettingChangedEventArgs(HandbrakeSettingId id, SettingValue value)
    {
        Id = id;
        Value = value;
    }

    public HandbrakeSettingId Id { get; }
    public SettingValue Value { get; }
}
