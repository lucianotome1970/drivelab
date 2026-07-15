// ============================================================================
//  DriveLab
//  FirmwareVersion.cs — Versão de firmware (release type, major, minor, patch) com serialização de/para bytes.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

public readonly record struct FirmwareVersion(byte ReleaseType, byte Major, byte Minor, byte Patch)
{
    public void WriteTo(Span<byte> dst)
    {
        dst[0] = ReleaseType;
        dst[1] = Major;
        dst[2] = Minor;
        dst[3] = Patch;
    }

    public static FirmwareVersion Parse(ReadOnlySpan<byte> src) =>
        new(src[0], src[1], src[2], src[3]);
}
