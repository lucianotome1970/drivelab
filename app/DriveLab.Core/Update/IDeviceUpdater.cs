// ============================================================================
//  DriveLab
//  IDeviceUpdater.cs — Contrato de atualização de firmware por USB para um
//  dispositivo DriveLab (base/pedal/handbrake/wheel): valida o arquivo,
//  manda o dispositivo entrar em bootloader DFU, espera ele reenumerar e
//  flasheia via dfu-util reportando progresso.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Update;

/// <summary>
/// Drives the USB firmware-update flow for one DriveLab device: validate the
/// selected firmware file, ask the device to jump into its DFU bootloader,
/// wait for the bootloader USB device to appear, then flash it (dfu-util)
/// while reporting progress.
/// </summary>
public interface IDeviceUpdater
{
    /// <summary>Which device this updater targets (must match the firmware file's signature).</summary>
    DeviceKind Kind { get; }

    /// <summary>Friendly name of the bootloader USB device this updater waits for, e.g. "STM32 BOOTLOADER (DFU)".</summary>
    string BootloaderName { get; }

    /// <summary>
    /// True if <paramref name="file"/> carries the DRVLABFW signature and it matches
    /// <see cref="Kind"/>. On false, <paramref name="error"/> holds a user-facing reason.
    /// </summary>
    bool ValidateFirmware(byte[] file, out string error);

    /// <summary>Sends the EnterDfu command over the device's normal transport, asking it to reboot into its bootloader.</summary>
    Task EnterBootloaderAsync();

    /// <summary>Polls for the bootloader's USB device to appear, up to <paramref name="timeout"/>. Returns true if found.</summary>
    Task<bool> WaitForBootloaderAsync(TimeSpan timeout);

    /// <summary>Flashes <paramref name="filePath"/> onto the device (already in bootloader mode) via dfu-util, reporting 0..1 progress.</summary>
    Task FlashAsync(string filePath, IProgress<double>? progress, CancellationToken ct = default);
}
