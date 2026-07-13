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

    public bool IsConnected => _transport.IsConnected;
    public FirmwareVersion FirmwareVersion => _transport.FirmwareVersion;

    public async Task ConnectAsync()
    {
        await _transport.ConnectAsync();
        // Streaming is currently a simulator capability; a future HidTransport
        // will expose an equivalent start/stop that this line will generalize to.
        (_transport as SimulatorTransport)?.StartStreaming();
    }

    public async Task DisconnectAsync()
    {
        (_transport as SimulatorTransport)?.StopStreaming();
        await _transport.DisconnectAsync();
    }

    public Task WriteSettingAsync(SettingId id, SettingValue value) => _transport.WriteSettingAsync(id, value);
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
