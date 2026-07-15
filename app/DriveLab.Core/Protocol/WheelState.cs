// ============================================================================
//  DriveLab
//  WheelState.cs — Telemetria do rim (fw, flags, bitmap de botões, pás de embreagem, deltas de encoder).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Buffers.Binary;

namespace DriveLab.Core.Protocol;

/// <summary>Leitura de uma pá analógica de embreagem: valor cru do ADC e saída após pipeline.</summary>
public readonly record struct WheelAxis(ushort Raw, ushort Output);

/// <summary>Telemetria do rim (report 0x21). Layout de 64 bytes little-endian espelhado pelo firmware-wheel.</summary>
public sealed class WheelState
{
    public const int EncoderCount = 4;

    public FirmwareVersion Firmware { get; set; }
    public WheelFlags Flags { get; set; }
    public uint Buttons { get; set; }
    public WheelAxis ClutchLeft { get; set; }
    public WheelAxis ClutchRight { get; set; }
    public sbyte[] EncoderDeltas { get; set; } = new sbyte[EncoderCount];

    public bool IsButtonPressed(int index) =>
        index is >= 0 and < 32 && (Buttons & (1u << index)) != 0;

    public byte[] ToBytes()
    {
        var buffer = new byte[ReportConstants.ReportSize];
        var span = buffer.AsSpan();
        Firmware.WriteTo(span.Slice(0, 4));
        span[4] = (byte)Flags;
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(5, 4), Buttons);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(9, 2), ClutchLeft.Raw);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(11, 2), ClutchLeft.Output);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(13, 2), ClutchRight.Raw);
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(15, 2), ClutchRight.Output);
        for (var i = 0; i < EncoderCount; i++)
            span[17 + i] = (byte)EncoderDeltas[i];
        return buffer;
    }

    public static WheelState Parse(ReadOnlySpan<byte> src)
    {
        var state = new WheelState
        {
            Firmware = FirmwareVersion.Parse(src.Slice(0, 4)),
            Flags = (WheelFlags)src[4],
            Buttons = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(5, 4)),
            ClutchLeft = new WheelAxis(
                BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(9, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(11, 2))),
            ClutchRight = new WheelAxis(
                BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(13, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(15, 2))),
        };
        for (var i = 0; i < EncoderCount; i++)
            state.EncoderDeltas[i] = (sbyte)src[17 + i];
        return state;
    }
}
