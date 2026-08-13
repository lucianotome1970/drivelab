// ============================================================================
//  DriveLab
//  CommandReport.cs — Report de comando (CommandId + Arg) serializado para bytes do protocolo USB.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

public sealed class CommandReport
{
    public byte CommandId { get; }
    public byte Arg { get; }

    public CommandReport(byte commandId, byte arg)
    {
        CommandId = commandId;
        Arg = arg;
    }

    public byte[] ToBytes()
    {
        var buffer = new byte[ReportConstants.ReportSize];
        buffer[0] = CommandId;
        buffer[1] = Arg;
        return buffer;
    }

    public static CommandReport Parse(ReadOnlySpan<byte> src) => new(src[0], src[1]);
}
