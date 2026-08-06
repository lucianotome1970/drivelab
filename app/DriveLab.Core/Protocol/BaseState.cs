// ============================================================================
//  DriveLab
//  BaseState.cs — Estado de telemetria do volante (firmware, flags, posição, ângulo, torque, temperaturas) serializado para bytes.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Buffers.Binary;

namespace DriveLab.Core.Protocol;

public sealed class BaseState
{
    public FirmwareVersion Firmware { get; set; }
    public BaseFlags Flags { get; set; }
    public short Position { get; set; }
    public short AngleDeciDeg { get; set; }
    public short Torque { get; set; }
    public short MotorCurrentMa { get; set; }
    public sbyte FetTempC { get; set; }
    public byte ErrorCode { get; set; }
    public ushort BusVoltageMv { get; set; }
    public sbyte MotorTempC { get; set; }
    public sbyte McuTempC { get; set; }

    /// <summary>Nível de clipping do FFB (0-255): quanto a força pedida pelo jogo passou do teto de torque e
    /// foi cortada. 0 = sem corte. Preenchido pelo firmware (medidor no engine).</summary>
    public byte Clipping { get; set; }

    /// <summary>Clipping em 0..100% (derivado de <see cref="Clipping"/>).</summary>
    public int ClippingPercent => (int)System.Math.Round(Clipping / 255.0 * 100);

    /// <summary>Energia total dissipada no resistor de freio desde o boot, em millijoules.</summary>
    public uint BrakeEnergyMilliJ { get; set; }

    /// <summary>Quantas vezes o chopper acionou desde o boot (eventos de frenagem, não ciclos de PWM).</summary>
    public uint BrakeActivations { get; set; }

    /// <summary>Maior potência instantânea vista no resistor desde o boot, em décimos de watt.</summary>
    public ushort BrakePeakDeciW { get; set; }

    /// <summary>Energia dissipada em joules (derivada de <see cref="BrakeEnergyMilliJ"/>).</summary>
    public double BrakeEnergyJoules => BrakeEnergyMilliJ / 1000.0;

    /// <summary>Pico de potência em watts (derivado de <see cref="BrakePeakDeciW"/>).</summary>
    public double BrakePeakWatts => BrakePeakDeciW / 10.0;


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
        span[13] = (byte)FetTempC;
        span[14] = ErrorCode;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(15, 2), BusVoltageMv);
        span[17] = (byte)MotorTempC;
        span[18] = (byte)McuTempC;
        span[19] = Clipping;
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..24], BrakeEnergyMilliJ);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..28], BrakeActivations);
        BinaryPrimitives.WriteUInt16LittleEndian(span[28..30], BrakePeakDeciW);
        return buffer;
    }

    public static BaseState Parse(ReadOnlySpan<byte> src) => new()
    {
        Firmware = FirmwareVersion.Parse(src.Slice(0, 4)),
        Flags = (BaseFlags)src[4],
        Position = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(5, 2)),
        AngleDeciDeg = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(7, 2)),
        Torque = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(9, 2)),
        MotorCurrentMa = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(11, 2)),
        FetTempC = (sbyte)src[13],
        ErrorCode = src[14],
        BusVoltageMv = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(15, 2)),
        MotorTempC = (sbyte)src[17],
        McuTempC = (sbyte)src[18],
        Clipping = src[19],
        BrakeEnergyMilliJ = BinaryPrimitives.ReadUInt32LittleEndian(src[20..24]),
        BrakeActivations = BinaryPrimitives.ReadUInt32LittleEndian(src[24..28]),
        BrakePeakDeciW = BinaryPrimitives.ReadUInt16LittleEndian(src[28..30]),
    };
}
