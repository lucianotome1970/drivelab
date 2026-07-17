// ============================================================================
//  DriveLab
//  IWheelTransport.cs — Contrato de transporte do volante removível (rim): telemetria, settings, comandos e LEDs via HID.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Core.Transport;

/// <summary>Transporte do volante removível (aro). Espelha <see cref="IHandbrakeTransport"/>, mas com
/// telemetria de <see cref="WheelState"/> (botões + embreagens + encoders) e uma saída extra de LEDs
/// (<see cref="WheelLedReport"/>). Reusa <see cref="WheelSettingId"/>/<see cref="WheelCommandId"/>.</summary>
public interface IWheelTransport
{
    bool IsConnected { get; }
    FirmwareVersion FirmwareVersion { get; }
    bool SupportsConfig { get; }

    /// <summary>Raised when new wheel telemetry is available. MAY fire on a background thread.</summary>
    event EventHandler<WheelState>? StateReceived;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task WriteSettingAsync(WheelSettingId id, SettingValue value);
    Task<SettingValue> ReadSettingAsync(WheelSettingId id);
    Task SendCommandAsync(WheelCommandId command, byte arg = 0);

    /// <summary>Envia as cores RGB do aro (WheelLed 0x18) — botões + barra, num único cordão.</summary>
    Task SendLedAsync(WheelLedReport led);

    /// <summary>Lê as cores pré-definidas guardadas no aro (pede via comando, recebe o report 0x19).</summary>
    Task<WheelLedReport> ReadLedsAsync();
}
