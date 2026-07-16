// ============================================================================
//  DriveLab
//  SettingValue.cs — Valor tipado de um setting, com serialização para bytes conforme o SettingType.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Buffers.Binary;

namespace DriveLab.Core.Settings;

public readonly struct SettingValue
{
    public SettingType Type { get; }
    public double AsDouble { get; }

    public SettingValue(SettingType type, double value)
    {
        Type = type;
        AsDouble = value;
    }

    public int WriteValue(Span<byte> dst)
    {
        // Inteiros: ARREDONDA (não trunca) — um slider dá double fracionário (ex.: 31.6) e o
        // rótulo mostra "32"; truncar gravaria 31 (bug do off-by-one no round-trip).
        switch (Type)
        {
            case SettingType.UInt8:
                dst[0] = (byte)Math.Round(AsDouble, MidpointRounding.AwayFromZero);
                return 1;
            case SettingType.Int8:
                dst[0] = (byte)(sbyte)Math.Round(AsDouble, MidpointRounding.AwayFromZero);
                return 1;
            case SettingType.UInt16:
                BinaryPrimitives.WriteUInt16LittleEndian(dst, (ushort)Math.Round(AsDouble, MidpointRounding.AwayFromZero));
                return 2;
            case SettingType.Int16:
                BinaryPrimitives.WriteInt16LittleEndian(dst, (short)Math.Round(AsDouble, MidpointRounding.AwayFromZero));
                return 2;
            case SettingType.Float:
                BinaryPrimitives.WriteSingleLittleEndian(dst, (float)AsDouble);
                return 4;
            default:
                throw new ArgumentOutOfRangeException(nameof(Type));
        }
    }

    public static SettingValue ReadValue(SettingType type, ReadOnlySpan<byte> src) => type switch
    {
        SettingType.UInt8 => new SettingValue(type, src[0]),
        SettingType.Int8 => new SettingValue(type, (sbyte)src[0]),
        SettingType.UInt16 => new SettingValue(type, BinaryPrimitives.ReadUInt16LittleEndian(src)),
        SettingType.Int16 => new SettingValue(type, BinaryPrimitives.ReadInt16LittleEndian(src)),
        SettingType.Float => new SettingValue(type, BinaryPrimitives.ReadSingleLittleEndian(src)),
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
