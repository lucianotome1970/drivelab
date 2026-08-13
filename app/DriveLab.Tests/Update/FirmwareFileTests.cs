// ============================================================================
//  DriveLab
//  FirmwareFileTests.cs — Testes de FirmwareFile: parsing da assinatura
//  DRVLABFW embutida em um .bin/.uf2 e validação contra o DeviceKind alvo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Text;
using DriveLab.Core.Update;

namespace DriveLab.Tests.Update;

public class FirmwareFileTests
{
    private static byte[] BuildSignature(DeviceKind kind, byte major, byte minor, byte patch,
        int prefixLength = 0, int suffixLength = 0)
    {
        var magic = Encoding.ASCII.GetBytes("DRVLABFW");
        var bytes = new byte[prefixLength + magic.Length + 1 + 3 + suffixLength];
        for (var i = 0; i < prefixLength; i++)
            bytes[i] = 0xAA;
        Array.Copy(magic, 0, bytes, prefixLength, magic.Length);
        var offset = prefixLength + magic.Length;
        bytes[offset] = (byte)kind;
        bytes[offset + 1] = major;
        bytes[offset + 2] = minor;
        bytes[offset + 3] = patch;
        for (var i = 0; i < suffixLength; i++)
            bytes[offset + 4 + i] = 0xBB;
        return bytes;
    }

    [Fact]
    public void Read_Finds_Base_Signature_With_Prefix_And_Suffix()
    {
        var bytes = BuildSignature(DeviceKind.Base, 0, 2, 0, prefixLength: 16, suffixLength: 32);

        var info = FirmwareFile.Read(bytes);

        Assert.True(info.Found);
        Assert.Equal(DeviceKind.Base, info.Kind);
        Assert.Equal(new Version(0, 2, 0), info.Version);
    }

    [Fact]
    public void Matches_True_For_Base_False_For_Pedal()
    {
        var bytes = BuildSignature(DeviceKind.Base, 0, 2, 0, prefixLength: 16, suffixLength: 32);

        Assert.True(FirmwareFile.Matches(bytes, DeviceKind.Base));
        Assert.False(FirmwareFile.Matches(bytes, DeviceKind.Pedal));
    }

    [Fact]
    public void Read_Finds_Pedal_Signature()
    {
        var bytes = BuildSignature(DeviceKind.Pedal, 1, 0, 3);

        var info = FirmwareFile.Read(bytes);

        Assert.True(info.Found);
        Assert.Equal(DeviceKind.Pedal, info.Kind);
        Assert.Equal(new Version(1, 0, 3), info.Version);
    }

    [Fact]
    public void Read_Without_Signature_Returns_NotFound()
    {
        var bytes = Encoding.ASCII.GetBytes("this is just some random firmware bytes without magic");

        var info = FirmwareFile.Read(bytes);

        Assert.False(info.Found);
    }

    [Fact]
    public void Matches_False_For_Any_Kind_When_Signature_Missing()
    {
        var bytes = Encoding.ASCII.GetBytes("no signature here at all");

        Assert.False(FirmwareFile.Matches(bytes, DeviceKind.Base));
        Assert.False(FirmwareFile.Matches(bytes, DeviceKind.Pedal));
        Assert.False(FirmwareFile.Matches(bytes, DeviceKind.Handbrake));
        Assert.False(FirmwareFile.Matches(bytes, DeviceKind.Wheel));
    }

    [Fact]
    public void Read_Truncated_Signature_At_End_Of_Buffer_Returns_NotFound_Without_Throwing()
    {
        var magic = Encoding.ASCII.GetBytes("DRVLABFW");
        // Magic present, but only 2 bytes follow (kind + 1 version byte) — not enough
        // for kind (1 byte) + version (3 bytes).
        var bytes = new byte[magic.Length + 2];
        Array.Copy(magic, bytes, magic.Length);
        bytes[magic.Length] = (byte)DeviceKind.Base;
        bytes[magic.Length + 1] = 0;

        var exception = Record.Exception(() => FirmwareFile.Read(bytes));

        Assert.Null(exception);
        Assert.False(FirmwareFile.Read(bytes).Found);
    }

    [Fact]
    public void Read_Empty_Buffer_Returns_NotFound_Without_Throwing()
    {
        var info = FirmwareFile.Read(Array.Empty<byte>());

        Assert.False(info.Found);
    }
}
