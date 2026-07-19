// ============================================================================
//  DriveLab
//  BaseUpdater.cs — IDeviceUpdater para a base (volante): manda EnterDfu pelo
//  transporte HID normal, espera a base reenumerar como bootloader STM32
//  (0483:df11, fora do HID — checado via `dfu-util -l`) e flasheia o .bin
//  chamando `dfu-util -a 0 -s 0x08000000:leave -D <arquivo>`, reportando
//  progresso a partir do stderr (ver DfuUtilProgress). Fluxo validado
//  manualmente na bancada nesta sessão; aqui só a plumbing — sem tocar
//  hardware real nos testes.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Diagnostics;
using DriveLab.Core.Transport;
using DriveLab.Core.Update;

namespace DriveLab.Hid.Update;

/// <summary><see cref="IDeviceUpdater"/> for the DriveLab base (steering wheel unit).</summary>
public sealed class BaseUpdater : IDeviceUpdater
{
    /// <summary>VID of the STM32 system bootloader (ST Microelectronics).</summary>
    public const int BootloaderVendorId = 0x0483;

    /// <summary>PID of the STM32 system bootloader in DFU mode.</summary>
    public const int BootloaderProductId = 0xdf11;

    /// <summary>dfu-util's `-a 0 -s 0x08000000:leave` alt-setting/address args used to flash the base's internal flash and reboot on completion.</summary>
    private const string DfuArgs = "-a 0 -s 0x08000000:leave";

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IBaseTransport _transport;
    private readonly string? _dfuUtilPathOverride;

    /// <param name="transport">Normal (HID) transport used to send EnterDfu before the device reboots into its bootloader.</param>
    /// <param name="dfuUtilPathOverride">Test seam: force a specific dfu-util path (or an invalid one) instead of auto-resolving.</param>
    public BaseUpdater(IBaseTransport transport, string? dfuUtilPathOverride = null)
    {
        _transport = transport;
        _dfuUtilPathOverride = dfuUtilPathOverride;
    }

    public DeviceKind Kind => DeviceKind.Base;

    public string BootloaderName => "STM32 BOOTLOADER (DFU)";

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
            error = $"Firmware é para '{info.Kind}', não para '{Kind}' (base).";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public Task EnterBootloaderAsync() => _transport.SendCommandAsync(BaseCommand.EnterDfu);

    /// <summary>
    /// Polls until the STM32 bootloader's USB device appears. DFU-mode devices are NOT HID
    /// devices (no HID report descriptor), so HidSharp's DeviceList cannot see them — this
    /// shells out to `dfu-util -l` instead and checks its output for "0483:df11".
    /// </summary>
    public async Task<bool> WaitForBootloaderAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            if (await IsBootloaderPresentAsync().ConfigureAwait(false))
                return true;

            await Task.Delay(PollInterval).ConfigureAwait(false);
        } while (DateTime.UtcNow < deadline);

        return await IsBootloaderPresentAsync().ConfigureAwait(false);
    }

    private async Task<bool> IsBootloaderPresentAsync()
    {
        var dfuUtilPath = ResolveDfuUtilPath();
        if (dfuUtilPath is null)
            return false;

        try
        {
            var psi = new ProcessStartInfo(dfuUtilPath, "-l")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
                return false;

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            var combined = stdout + stderr;
            return combined.Contains("0483:df11", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Flashes <paramref name="filePath"/> onto the device (already in bootloader mode) by
    /// shelling out to `dfu-util -a 0 -s 0x08000000:leave -D &lt;file&gt;`. dfu-util writes its
    /// progress bar to STDERR (verified: `Download\t[====] NN%` lines appear on stderr, not
    /// stdout); both streams are still read concurrently to avoid deadlocking on a full pipe
    /// buffer, and every non-empty line from either stream is fed to <see cref="DfuUtilProgress.Parse"/>.
    /// </summary>
    public async Task FlashAsync(string filePath, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Arquivo de firmware não encontrado.", filePath);

        var dfuUtilPath = ResolveDfuUtilPath()
            ?? throw new InvalidOperationException(
                "dfu-util não encontrado. Instale-o (ex.: 'brew install dfu-util' no macOS) e garanta que está no PATH.");

        var psi = new ProcessStartInfo(dfuUtilPath, $"{DfuArgs} -D \"{filePath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var sawDone = false;
        var stderrLines = new List<string>();

        void OnLine(string? line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            stderrLines.Add(line);
            if (line.Contains("File downloaded successfully", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Download done.", StringComparison.OrdinalIgnoreCase))
            {
                sawDone = true;
            }

            var fraction = DfuUtilProgress.Parse(line);
            if (fraction is not null)
                progress?.Report(fraction.Value);
        }

        process.OutputDataReceived += (_, e) => OnLine(e.Data);
        process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        if (!process.Start())
            throw new InvalidOperationException("Falha ao iniciar dfu-util.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using (ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process may have already exited between the check and Kill.
            }
        }))
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();

        if (process.ExitCode != 0)
        {
            var tail = string.Join('\n', stderrLines.TakeLast(10));
            throw new InvalidOperationException($"dfu-util falhou (exit code {process.ExitCode}):\n{tail}");
        }

        if (!sawDone)
        {
            var tail = string.Join('\n', stderrLines.TakeLast(10));
            throw new InvalidOperationException($"dfu-util terminou sem confirmar o download:\n{tail}");
        }

        progress?.Report(1.0);
    }

    /// <summary>
    /// Resolves the dfu-util binary: explicit override (tests) → common Homebrew paths →
    /// PATH lookup (`which`/`where`). Returns null if none exist/succeed.
    /// </summary>
    private string? ResolveDfuUtilPath()
    {
        if (_dfuUtilPathOverride is not null)
            return File.Exists(_dfuUtilPathOverride) ? _dfuUtilPathOverride : null;

        string[] candidates =
        [
            "/opt/homebrew/bin/dfu-util", // Apple Silicon Homebrew
            "/usr/local/bin/dfu-util",    // Intel Homebrew / typical Linux
        ];
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return FindOnPath();
    }

    private static string? FindOnPath()
    {
        try
        {
            var whichCmd = OperatingSystem.IsWindows() ? "where" : "which";
            var psi = new ProcessStartInfo(whichCmd, "dfu-util")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                return null;

            var path = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return path is not null && File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }
}
