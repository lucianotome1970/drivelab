// ============================================================================
//  DriveLab
//  IDeviceAccessCoordinator.cs — Deixa o fluxo de atualização tomar controle
//  EXCLUSIVO da USB de um dispositivo durante o update: pausa o auto-connect
//  do host e solta o handle do transporte normal (HID), para o dispositivo
//  poder re-enumerar entrando e saindo do bootloader (DFU) sem que outro ator
//  (thread de leitura HID / DeviceAutoConnector) reabra o device no meio.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Update;

/// <summary>
/// Coordinates exclusive USB access for a firmware update. Without this, the app keeps the
/// device's normal (HID) handle open and an auto-connect poller re-grabs it the moment it
/// re-appears — both prevent the device from cleanly re-enumerating as its DFU bootloader, so
/// <c>dfu-util</c> never sees it. The update flow calls <see cref="BeginExclusiveAsync"/> after
/// sending EnterDfu (to pause auto-connect and release the handle) and always calls
/// <see cref="EndExclusiveAsync"/> when done (to resume auto-connect, which reconnects the
/// device once it reboots into the new firmware).
/// </summary>
public interface IDeviceAccessCoordinator
{
    /// <summary>Pause auto-connect for <paramref name="kind"/> and release its normal transport handle.
    /// Safe to call for a device with no host-side management (no-op).</summary>
    Task BeginExclusiveAsync(DeviceKind kind);

    /// <summary>Resume normal auto-connect for <paramref name="kind"/>. Idempotent — safe to call even if
    /// <see cref="BeginExclusiveAsync"/> was never called (e.g. EnterDfu threw first).</summary>
    Task EndExclusiveAsync(DeviceKind kind);
}
