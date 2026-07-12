using System.Buffers.Binary;

namespace DriveLab.Core.Protocol;

public sealed class DeviceState
{
    public FirmwareVersion Firmware { get; set; }
    public DeviceFlags Flags { get; set; }
    public short Position { get; set; }
    public short AngleDeciDeg { get; set; }
    public short Torque { get; set; }
    public short MotorCurrentMa { get; set; }
    public sbyte TemperatureC { get; set; }
    public byte ErrorCode { get; set; }

    public byte[] ToBytes()
    {
        var buffer = new byte[ReportConstants.ReportSize];
        var span = buffer.AsSpan();
        Firmware.WriteTo(span.Slice(0, 4));
        span[4] = (byte)Flags;
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(5, 2), Position);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(7, 2), AngleDeciDeg);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(9, 2), Torque);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(11, 2), MotorCurrentMa);
        span[13] = (byte)TemperatureC;
        span[14] = ErrorCode;
        return buffer;
    }

    public static DeviceState Parse(ReadOnlySpan<byte> src) => new()
    {
        Firmware = FirmwareVersion.Parse(src.Slice(0, 4)),
        Flags = (DeviceFlags)src[4],
        Position = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(5, 2)),
        AngleDeciDeg = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(7, 2)),
        Torque = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(9, 2)),
        MotorCurrentMa = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(11, 2)),
        TemperatureC = (sbyte)src[13],
        ErrorCode = src[14],
    };
}
