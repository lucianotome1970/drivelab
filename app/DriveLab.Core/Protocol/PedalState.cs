using System.Buffers.Binary;
using DriveLab.Core.Settings;

namespace DriveLab.Core.Protocol;

public readonly record struct PedalReading(ushort RawInput, ushort Output);

public sealed class PedalState
{
    public FirmwareVersion Firmware { get; set; }
    public byte Flags { get; set; }
    public PedalReading Clutch { get; set; }
    public PedalReading Brake { get; set; }
    public PedalReading Throttle { get; set; }

    public PedalReading this[PedalIndex pedal] => pedal switch
    {
        PedalIndex.Clutch => Clutch,
        PedalIndex.Brake => Brake,
        PedalIndex.Throttle => Throttle,
        _ => throw new ArgumentOutOfRangeException(nameof(pedal)),
    };

    public byte[] ToBytes()
    {
        var buffer = new byte[ReportConstants.ReportSize];
        var span = buffer.AsSpan();
        Firmware.WriteTo(span.Slice(0, 4));
        span[4] = Flags;
        WriteReading(span.Slice(5, 4), Clutch);
        WriteReading(span.Slice(9, 4), Brake);
        WriteReading(span.Slice(13, 4), Throttle);
        return buffer;
    }

    public static PedalState Parse(ReadOnlySpan<byte> src) => new()
    {
        Firmware = FirmwareVersion.Parse(src.Slice(0, 4)),
        Flags = src[4],
        Clutch = ReadReading(src.Slice(5, 4)),
        Brake = ReadReading(src.Slice(9, 4)),
        Throttle = ReadReading(src.Slice(13, 4)),
    };

    private static void WriteReading(Span<byte> dst, PedalReading r)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(0, 2), r.RawInput);
        BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(2, 2), r.Output);
    }

    private static PedalReading ReadReading(ReadOnlySpan<byte> src) =>
        new(BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(0, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(2, 2)));
}
