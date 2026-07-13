using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Simulator;

namespace DriveLab.Studio.Services;

/// <summary>Fachada sobre um <see cref="IPedalTransport"/>, marshalando telemetria p/ a thread UI.</summary>
public sealed class PedalDeviceSession : IDisposable
{
    private readonly IPedalTransport _transport;
    private readonly IUiDispatcher _dispatcher;

    public PedalDeviceSession(IPedalTransport transport, IUiDispatcher dispatcher)
    {
        _transport = transport;
        _dispatcher = dispatcher;
        _transport.StateReceived += OnTransportState;
    }

    public event EventHandler<PedalState>? StateReceived;
    public event EventHandler? Connected;
    public event EventHandler? Disconnected;
    public event EventHandler<PedalSettingChangedEventArgs>? SettingChanged;

    public bool IsConnected => _transport.IsConnected;
    public FirmwareVersion FirmwareVersion => _transport.FirmwareVersion;

    public async Task ConnectAsync()
    {
        await _transport.ConnectAsync();
        (_transport as SimulatorPedalTransport)?.StartStreaming();
        Connected?.Invoke(this, EventArgs.Empty);
    }

    public async Task DisconnectAsync()
    {
        (_transport as SimulatorPedalTransport)?.StopStreaming();
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
        (_transport as SimulatorPedalTransport)?.StopStreaming();
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
