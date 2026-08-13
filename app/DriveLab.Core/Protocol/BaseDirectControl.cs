// ============================================================================
//  DriveLab
//  BaseDirectControl.cs — Report de controle direto de forças (spring/constant/periodic/damper) serializado para bytes.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Buffers.Binary;

namespace DriveLab.Core.Protocol;

public sealed class BaseDirectControl
{
    public short SpringForce { get; set; }
    public short ConstantForce { get; set; }
    public short PeriodicForce { get; set; }
    public short DamperForce { get; set; }
    public byte ForceDrop { get; set; }

    /// <summary>Força aditiva de efeitos por telemetria (−255..255, unidades force255). O firmware a soma à
    /// demanda (engine.setTelemetryForce), convivendo com o FFB do jogo. Ver <see cref="Telemetry.Effects"/>.</summary>
    public short TelemetryForce { get; set; }

    public byte[] ToBytes()
    {
        var buffer = new byte[ReportConstants.ReportSize];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(0, 2), SpringForce);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(2, 2), ConstantForce);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(4, 2), PeriodicForce);
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(6, 2), DamperForce);
        span[8] = ForceDrop;
        BinaryPrimitives.WriteInt16LittleEndian(span.Slice(9, 2), TelemetryForce);
        return buffer;
    }

    public static BaseDirectControl Parse(ReadOnlySpan<byte> src) => new()
    {
        SpringForce = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(0, 2)),
        ConstantForce = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(2, 2)),
        PeriodicForce = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(4, 2)),
        DamperForce = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(6, 2)),
        ForceDrop = src[8],
        TelemetryForce = BinaryPrimitives.ReadInt16LittleEndian(src.Slice(9, 2)),
    };
}
