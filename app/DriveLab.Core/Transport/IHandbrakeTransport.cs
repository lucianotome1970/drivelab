using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Core.Transport;

/// <summary>Transporte do freio de mão. Espelha IPedalTransport, mas de eixo único
/// (sem PedalIndex). Reusa PedalCommandId (Calibrate/Save/LoadDefaults).</summary>
public interface IHandbrakeTransport
{
    bool IsConnected { get; }
    FirmwareVersion FirmwareVersion { get; }
    bool SupportsConfig { get; }

    /// <summary>Raised when new handbrake telemetry is available. MAY fire on a background thread.</summary>
    event EventHandler<PedalState>? StateReceived;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task WriteSettingAsync(HandbrakeSettingId id, SettingValue value);
    Task<SettingValue> ReadSettingAsync(HandbrakeSettingId id);
    Task SendCommandAsync(PedalCommandId command, byte arg = 0);
}
