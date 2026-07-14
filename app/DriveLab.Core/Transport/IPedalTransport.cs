using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Core.Transport;

public interface IPedalTransport
{
    bool IsConnected { get; }
    FirmwareVersion FirmwareVersion { get; }

    /// <summary>True quando o app pode configurar o dispositivo (nossos pedais/simulador);
    /// false para fontes read-only (ex.: Simagic — só leitura).</summary>
    bool SupportsConfig { get; }

    /// <summary>Raised when new pedal telemetry is available. MAY fire on a background thread.</summary>
    event EventHandler<PedalState>? StateReceived;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task WriteSettingAsync(PedalSettingId id, PedalIndex pedal, SettingValue value);
    Task<SettingValue> ReadSettingAsync(PedalSettingId id, PedalIndex pedal);
    Task SendCommandAsync(PedalCommandId command, byte arg = 0);
}
