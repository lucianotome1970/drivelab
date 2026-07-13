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
