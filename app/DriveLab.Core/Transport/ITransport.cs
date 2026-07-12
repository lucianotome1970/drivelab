using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Core.Transport;

public interface ITransport
{
    bool IsConnected { get; }
    FirmwareVersion FirmwareVersion { get; }

    event EventHandler<DeviceState>? StateReceived;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task WriteSettingAsync(SettingId id, SettingValue value);
    Task<SettingValue> ReadSettingAsync(SettingId id);
    Task SendDirectControlAsync(DirectControl control);
    Task SendCommandAsync(DeviceCommand command, byte arg = 0);
}
