// ============================================================================
//  DriveLab
//  Rp2040UpdaterTests.cs — Testes de Rp2040Updater: validação de firmware,
//  envio do comando de entrada em bootloader, poll do volume RPI-RP2 e
//  caminhos de erro/sucesso do FlashAsync, tudo via seams (sem tocar
//  hardware/volume real).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Text;
using DriveLab.Core.Update;
using DriveLab.Hid.Update;
using Xunit;

namespace DriveLab.Hid.Tests.Update;

public class Rp2040UpdaterTests
{
    private static byte[] BuildSignature(DeviceKind kind, byte major = 0, byte minor = 1, byte patch = 0)
    {
        var magic = Encoding.ASCII.GetBytes("DRVLABFW");
        var bytes = new byte[magic.Length + 4];
        Array.Copy(magic, bytes, magic.Length);
        bytes[magic.Length] = (byte)kind;
        bytes[magic.Length + 1] = major;
        bytes[magic.Length + 2] = minor;
        bytes[magic.Length + 3] = patch;
        return bytes;
    }

    [Fact]
    public void Kind_Is_Set_And_BootloaderName_Is_Friendly()
    {
        var updater = new Rp2040Updater(DeviceKind.Pedal, () => Task.CompletedTask);

        Assert.Equal(DeviceKind.Pedal, updater.Kind);
        Assert.Equal("RP2040 BOOTSEL (RPI-RP2)", updater.BootloaderName);
    }

    [Fact]
    public void ValidateFirmware_True_For_Matching_Kind_Signature()
    {
        var updater = new Rp2040Updater(DeviceKind.Pedal, () => Task.CompletedTask);
        var file = BuildSignature(DeviceKind.Pedal);

        var ok = updater.ValidateFirmware(file, out var error);

        Assert.True(ok);
        Assert.Empty(error);
    }

    [Fact]
    public void ValidateFirmware_False_For_Other_Device_Kind()
    {
        var updater = new Rp2040Updater(DeviceKind.Pedal, () => Task.CompletedTask);
        var file = BuildSignature(DeviceKind.Handbrake);

        var ok = updater.ValidateFirmware(file, out var error);

        Assert.False(ok);
        Assert.NotEmpty(error);
        Assert.Contains("Handbrake", error);
        Assert.Contains("Pedal", error);
    }

    [Fact]
    public void ValidateFirmware_False_When_No_Signature()
    {
        var updater = new Rp2040Updater(DeviceKind.Pedal, () => Task.CompletedTask);
        var file = Encoding.ASCII.GetBytes("not a firmware file at all");

        var ok = updater.ValidateFirmware(file, out var error);

        Assert.False(ok);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task EnterBootloaderAsync_Invokes_The_Delegate()
    {
        var called = false;
        var updater = new Rp2040Updater(DeviceKind.Pedal, () =>
        {
            called = true;
            return Task.CompletedTask;
        });

        await updater.EnterBootloaderAsync();

        Assert.True(called);
    }

    [Fact]
    public async Task WaitForBootloaderAsync_Returns_False_When_Volume_Never_Appears()
    {
        var updater = new Rp2040Updater(DeviceKind.Pedal, () => Task.CompletedTask, findVolume: () => null);

        var found = await updater.WaitForBootloaderAsync(TimeSpan.FromMilliseconds(200));

        Assert.False(found);
    }

    [Fact]
    public async Task WaitForBootloaderAsync_Returns_True_Quickly_When_Volume_Present()
    {
        var updater = new Rp2040Updater(DeviceKind.Pedal, () => Task.CompletedTask, findVolume: () => "/tmp/rp2");
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var found = await updater.WaitForBootloaderAsync(TimeSpan.FromSeconds(5));

        sw.Stop();
        Assert.True(found);
        Assert.True(sw.ElapsedMilliseconds < 1000, $"Expected quick return, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task WaitForBootloaderAsync_Respects_Small_Timeout_Instead_Of_Overrunning()
    {
        var updater = new Rp2040Updater(DeviceKind.Pedal, () => Task.CompletedTask, findVolume: () => null);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var found = await updater.WaitForBootloaderAsync(TimeSpan.FromMilliseconds(150));

        sw.Stop();
        Assert.False(found);
        Assert.True(sw.ElapsedMilliseconds < 400, $"Expected < 400ms, took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task FlashAsync_Copies_File_To_Volume_And_Reports_Progress()
    {
        var tempDir = Directory.CreateTempSubdirectory("rp2040updater-test-");
        var srcFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(srcFile, BuildSignature(DeviceKind.Pedal));

            string? recordedSrc = null;
            string? recordedDst = null;
            var updater = new Rp2040Updater(
                DeviceKind.Pedal,
                () => Task.CompletedTask,
                findVolume: () => tempDir.FullName,
                copyFile: (src, dst) =>
                {
                    recordedSrc = src;
                    recordedDst = dst;
                    File.Copy(src, dst, overwrite: true);
                    return Task.CompletedTask;
                });

            var progressValues = new List<double>();
            var progress = new Progress<double>(v => progressValues.Add(v));

            await updater.FlashAsync(srcFile, progress);

            Assert.Equal(srcFile, recordedSrc);
            Assert.Equal(Path.Combine(tempDir.FullName, "firmware.uf2"), recordedDst);
            Assert.True(File.Exists(Path.Combine(tempDir.FullName, "firmware.uf2")));
            Assert.Contains(1.0, progressValues);
        }
        finally
        {
            File.Delete(srcFile);
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task FlashAsync_Throws_InvalidOperationException_When_Volume_Not_Found()
    {
        var srcFile = Path.GetTempFileName();
        try
        {
            var updater = new Rp2040Updater(DeviceKind.Pedal, () => Task.CompletedTask, findVolume: () => null);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                updater.FlashAsync(srcFile, progress: null));

            Assert.Contains("RPI-RP2", ex.Message);
        }
        finally
        {
            File.Delete(srcFile);
        }
    }

    [Fact]
    public async Task FlashAsync_Throws_FileNotFoundException_When_Firmware_File_Missing()
    {
        var updater = new Rp2040Updater(DeviceKind.Pedal, () => Task.CompletedTask, findVolume: () => "/tmp");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            updater.FlashAsync("/no/such/file.uf2", progress: null));
    }

    // --- Tolerância à ejeção do volume durante a gravação do .uf2 ---

    [Fact]
    public void CopyToleratingEject_Success_When_CopyDoesNotThrow()
    {
        var ex = Record.Exception(() =>
            Rp2040Updater.CopyToleratingEject("/Volumes/RPI-RP2/firmware.uf2", copy: () => { }, volumeExists: _ => true));
        Assert.Null(ex);
    }

    [Fact]
    public void CopyToleratingEject_Success_When_VolumeEjectedMidCopy()
    {
        // Copy lança IOException (volume sumiu no flush) E o volume não existe mais → é sucesso (gravou/reiniciou).
        var ex = Record.Exception(() => Rp2040Updater.CopyToleratingEject(
            "/Volumes/RPI-RP2/firmware.uf2",
            copy: () => throw new IOException("device not configured"),
            volumeExists: _ => false));
        Assert.Null(ex);
    }

    [Fact]
    public void CopyToleratingEject_Rethrows_When_VolumeStillMounted()
    {
        // Erro de verdade: copy falhou mas o volume continua montado → repropaga.
        Assert.Throws<IOException>(() => Rp2040Updater.CopyToleratingEject(
            "/Volumes/RPI-RP2/firmware.uf2",
            copy: () => throw new IOException("disco cheio"),
            volumeExists: _ => true));
    }
}
