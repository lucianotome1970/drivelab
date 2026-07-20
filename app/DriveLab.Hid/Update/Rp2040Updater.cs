// ============================================================================
//  DriveLab
//  Rp2040Updater.cs — IDeviceUpdater genérico para dispositivos RP2040
//  (pedal/handbrake/wheel): manda o comando de reboot para bootloader pelo
//  transporte normal (P0), espera o volume de armazenamento em massa
//  RPI-RP2 montar (BOOTSEL) e flasheia copiando o .uf2 para dentro dele
//  como "firmware.uf2" — o próprio bootloader do RP2040 grava a flash e
//  reinicia ao detectar a cópia. Sem tocar hardware real nos testes: as
//  três dependências externas (entrar em bootloader, achar o volume,
//  copiar o arquivo) são seams injetáveis.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Update;

namespace DriveLab.Hid.Update;

/// <summary><see cref="IDeviceUpdater"/> genérico para dispositivos RP2040 (UF2/BOOTSEL).</summary>
public sealed class Rp2040Updater : IDeviceUpdater
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly Func<Task> _enterBootloader;
    private readonly Func<string?> _findVolume;
    private readonly Func<string, string, Task> _copyFile;

    /// <param name="kind">Dispositivo alvo (deve bater com o kind embutido no firmware).</param>
    /// <param name="enterBootloader">Manda o comando (P0) que pede ao dispositivo pra entrar em BOOTSEL.</param>
    /// <param name="findVolume">Test seam: retorna o caminho do volume RPI-RP2 montado, ou null. Default: escaneia /Volumes.</param>
    /// <param name="copyFile">Test seam: copia o arquivo de origem para o destino. Default: File.Copy em background thread.</param>
    public Rp2040Updater(
        DeviceKind kind,
        Func<Task> enterBootloader,
        Func<string?>? findVolume = null,
        Func<string, string, Task>? copyFile = null)
    {
        Kind = kind;
        _enterBootloader = enterBootloader;
        _findVolume = findVolume ?? DefaultFindVolume;
        _copyFile = copyFile ?? DefaultCopyFile;
    }

    public DeviceKind Kind { get; }

    public string BootloaderName => "RP2040 BOOTSEL (RPI-RP2)";

    public bool ValidateFirmware(byte[] file, out string error)
    {
        var info = FirmwareFile.Read(file);
        if (!info.Found)
        {
            error = "Arquivo não é um firmware DriveLab reconhecido (assinatura DRVLABFW não encontrada).";
            return false;
        }

        if (info.Kind != Kind)
        {
            error = $"Firmware é para '{info.Kind}', não '{Kind}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public Task EnterBootloaderAsync() => _enterBootloader();

    /// <summary>Polls <c>findVolume()</c> every ~250ms until the RPI-RP2 volume appears or the timeout elapses.</summary>
    public async Task<bool> WaitForBootloaderAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (_findVolume() is not null)
                return true;

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            await Task.Delay(remaining < PollInterval ? remaining : PollInterval).ConfigureAwait(false);
        } while (DateTime.UtcNow < deadline);

        return _findVolume() is not null;
    }

    /// <summary>
    /// Flasheia copiando <paramref name="filePath"/> para <c>&lt;volume&gt;/firmware.uf2</c>. O
    /// bootloader RP2040 grava a flash e reinicia sozinho assim que a cópia termina.
    /// </summary>
    public async Task FlashAsync(string filePath, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Arquivo de firmware não encontrado.", filePath);

        var volume = _findVolume()
            ?? throw new InvalidOperationException(
                "Volume RPI-RP2 não encontrado — a placa entrou em BOOTSEL?");

        ct.ThrowIfCancellationRequested();

        progress?.Report(0);
        var destination = Path.Combine(volume, "firmware.uf2");
        await _copyFile(filePath, destination).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        progress?.Report(1.0);
    }

    private static string? DefaultFindVolume()
    {
        try
        {
            if (!Directory.Exists("/Volumes"))
                return null;

            return Directory.GetDirectories("/Volumes")
                .FirstOrDefault(d => Path.GetFileName(d).StartsWith("RPI-RP2", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }

    private static Task DefaultCopyFile(string src, string dst) =>
        Task.Run(() => File.Copy(src, dst, overwrite: true));
}
