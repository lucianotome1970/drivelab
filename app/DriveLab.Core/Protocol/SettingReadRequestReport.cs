namespace DriveLab.Core.Protocol;

public sealed class SettingReadRequestReport
{
    public byte FieldId { get; }
    public byte Index { get; }

    public SettingReadRequestReport(byte fieldId, byte index)
    {
        FieldId = fieldId;
        Index = index;
    }

    public byte[] ToBytes()
    {
        var buffer = new byte[ReportConstants.ReportSize];
        buffer[0] = FieldId;
        buffer[1] = Index;
        return buffer;
    }

    public static SettingReadRequestReport Parse(ReadOnlySpan<byte> src) => new(src[0], src[1]);
}
