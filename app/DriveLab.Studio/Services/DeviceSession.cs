using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Simulator;

namespace DriveLab.Studio.Services;

/// <summary>
/// App-facing facade over an <see cref="ITransport"/>. Marshals device telemetry
/// onto the UI thread via <see cref="IUiDispatcher"/> so ViewModels can bind safely.
/// </summary>
public sealed class DeviceSession : IDisposable
{
    private readonly ITransport _transport;
    private readonly IUiDispatcher _dispatcher;

    public DeviceSession(ITransport transport, IUiDispatcher dispatcher)
    {
        _transport = transport;
        _dispatcher = dispatcher;
        _transport.StateReceived += OnTransportState;
    }

    public event EventHandler<DeviceState>? StateReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;

    /// <summary>
    /// Raised (on the UI thread) after a setting is written, so every view bound to
    /// that setting stays in sync regardless of which view triggered the change.
    /// </summary>
    public event EventHandler<SettingChangedEventArgs>? SettingChanged;

    public bool IsConnected => _transport.IsConnected;
    public FirmwareVersion FirmwareVersion => _transport.FirmwareVersion;

    public async Task ConnectAsync()
    {
        await _transport.ConnectAsync();

        // Se o transporte não abriu (ex.: hardware ausente no modo real), não dispara
        // Connected — evita que views leiam settings num canal fechado (timeout/crash).
        if (!_transport.IsConnected)
            return;

        // Streaming is currently a simulator capability; a future HidTransport
        // will expose an equivalent start/stop that this line will generalize to.
        (_transport as SimulatorTransport)?.StartStreaming();
        Connected?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync()
    {
        (_transport as SimulatorTransport)?.StopStreaming();
        await _transport.DisconnectAsync();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public async Task WriteSettingAsync(SettingId id, SettingValue value)
    {
        await _transport.WriteSettingAsync(id, value);
        _dispatcher.Post(() => SettingChanged?.Invoke(this, new SettingChangedEventArgs(id, value)));
    }

    public Task<SettingValue> ReadSettingAsync(SettingId id) => _transport.ReadSettingAsync(id);
    public Task SendDirectControlAsync(DirectControl control) => _transport.SendDirectControlAsync(control);
    public Task SendCommandAsync(DeviceCommand command, byte arg = 0) => _transport.SendCommandAsync(command, arg);

    private void OnTransportState(object? sender, DeviceState state) =>
        _dispatcher.Post(() => StateReceived?.Invoke(this, state));

    public void Dispose()
    {
        _transport.StateReceived -= OnTransportState;
        (_transport as SimulatorTransport)?.StopStreaming();
    }
}

/// <summary>Payload for <see cref="DeviceSession.SettingChanged"/>.</summary>
public sealed class SettingChangedEventArgs : EventArgs
{
    public SettingChangedEventArgs(SettingId id, SettingValue value)
    {
        Id = id;
        Value = value;
    }

    public SettingId Id { get; }
    public SettingValue Value { get; }
}
