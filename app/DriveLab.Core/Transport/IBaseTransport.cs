// ============================================================================
//  DriveLab
//  IBaseTransport.cs — Contrato de transporte do volante: conexão, telemetria de estado e envio de comandos via HID.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Core.Transport;

public interface IBaseTransport
{
    bool IsConnected { get; }
    FirmwareVersion FirmwareVersion { get; }

    /// <summary>
    /// Raised whenever a new device state is available. This event MAY be raised on a
    /// background thread (e.g. a streaming timer thread) rather than the thread that
    /// subscribed to it. Handlers must marshal to their own thread (e.g. a UI dispatcher)
    /// if they need to touch thread-affine state.
    /// </summary>
    event EventHandler<BaseState>? StateReceived;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
    Task WriteSettingAsync(BaseSettingId id, SettingValue value);
    Task<SettingValue> ReadSettingAsync(BaseSettingId id);
    Task SendDirectControlAsync(BaseDirectControl control);
    Task SendCommandAsync(BaseCommand command, byte arg = 0);
}
