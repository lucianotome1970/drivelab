// ============================================================================
//  DriveLab
//  BaseUpdaterTests.cs — Testes de BaseUpdater: validação de firmware, envio
//  de EnterDfu e caminhos de erro do FlashAsync (arquivo/dfu-util ausentes)
//  sem tocar hardware real.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Text;
using DriveLab.Core.Transport;
using DriveLab.Core.Update;
using DriveLab.Hid.Update;
using Xunit;

namespace DriveLab.Hid.Tests.Update;

public class BaseUpdaterTests
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
    public void Kind_Is_Base_And_BootloaderName_Is_Friendly()
    {
        var updater = new BaseUpdater(new FakeBaseTransport());

        Assert.Equal(DeviceKind.Base, updater.Kind);
        Assert.Equal("STM32 BOOTLOADER (DFU)", updater.BootloaderName);
    }

    [Fact]
    public void ValidateFirmware_True_For_Matching_Base_Signature()
    {
        var updater = new BaseUpdater(new FakeBaseTransport());
        var file = BuildSignature(DeviceKind.Base);

        var ok = updater.ValidateFirmware(file, out var error);

        Assert.True(ok);
        Assert.Empty(error);
    }

    [Fact]
    public void ValidateFirmware_False_For_Other_Device_Kind()
    {
        var updater = new BaseUpdater(new FakeBaseTransport());
        var file = BuildSignature(DeviceKind.Pedal);

        var ok = updater.ValidateFirmware(file, out var error);

        Assert.False(ok);
        Assert.NotEmpty(error);
        Assert.Contains("Pedal", error);
    }

    [Fact]
    public void ValidateFirmware_False_When_No_Signature()
    {
        var updater = new BaseUpdater(new FakeBaseTransport());
        var file = Encoding.ASCII.GetBytes("not a firmware file at all");

        var ok = updater.ValidateFirmware(file, out var error);

        Assert.False(ok);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task EnterBootloaderAsync_Sends_EnterDfu_Command()
    {
        var transport = new FakeBaseTransport();
        var updater = new BaseUpdater(transport);

        await updater.EnterBootloaderAsync();

        Assert.Equal(BaseCommand.EnterDfu, transport.LastCommand?.cmd);
    }

    [Fact]
    public async Task FlashAsync_Throws_FileNotFoundException_When_Firmware_File_Missing()
    {
        var updater = new BaseUpdater(new FakeBaseTransport());

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            updater.FlashAsync("/no/such/file.bin", progress: null));
    }

    [Fact]
    public async Task FlashAsync_Throws_Clear_Error_When_DfuUtil_Missing()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Override with a path that does not exist to simulate "dfu-util not installed"
            // without depending on (or polluting) the real PATH/Homebrew lookup.
            var updater = new BaseUpdater(new FakeBaseTransport(), dfuUtilPathOverride: "/no/such/dfu-util");

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                updater.FlashAsync(tempFile, progress: null));

            Assert.Contains("dfu-util", ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task WaitForBootloaderAsync_Returns_False_Quickly_When_DfuUtil_Missing()
    {
        var updater = new BaseUpdater(new FakeBaseTransport(), dfuUtilPathOverride: "/no/such/dfu-util");

        var found = await updater.WaitForBootloaderAsync(TimeSpan.FromMilliseconds(200));

        Assert.False(found);
    }
}
