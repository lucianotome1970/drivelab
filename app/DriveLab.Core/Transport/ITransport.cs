using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Core.Transport;

public interface ITransport
{
    bool IsConnected { get; }
    FirmwareVersion FirmwareVersion { get; }

    /// <summary>
    /// Raised whenever a new device state is available. This event MAY be raised on a
    /// background thread (e.g. a streaming timer thread) rather than the thread that
    /// subscribed to it. Handlers must marshal to their own thread (e.g. a UI dispatcher)
    /// if they need to touch thread-affine state.
    /// </summary>
    event EventHandler<DeviceState>? StateReceived;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task WriteSettingAsync(SettingId id, SettingValue value);
    Task<SettingValue> ReadSettingAsync(SettingId id);
    Task SendDirectControlAsync(DirectControl control);
    Task SendCommandAsync(DeviceCommand command, byte arg = 0);
}
