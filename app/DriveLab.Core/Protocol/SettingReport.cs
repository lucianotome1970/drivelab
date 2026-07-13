using DriveLab.Core.Settings;

namespace DriveLab.Core.Protocol;

/// <summary>Wire report for a single setting (used for SettingWrite out and SettingValue in). Payload: FieldId, Index, ValueType, value bytes.</summary>
public sealed class SettingReport
{
    public byte FieldId { get; }
    public byte Index { get; }
    public SettingValue Value { get; }

    public SettingReport(byte fieldId, byte index, SettingValue value)
    {
        FieldId = fieldId;
        Index = index;
        Value = value;
    }

    public byte[] ToBytes()
    {
        var buffer = new byte[ReportConstants.ReportSize];
        buffer[0] = FieldId;
        buffer[1] = Index;
        buffer[2] = (byte)Value.Type;
        Value.WriteValue(buffer.AsSpan(3));
        return buffer;
    }

    public static SettingReport Parse(ReadOnlySpan<byte> src)
    {
        var fieldId = src[0];
        var index = src[1];
        var type = (SettingType)src[2];
        var value = SettingValue.ReadValue(type, src.Slice(3));
        return new SettingReport(fieldId, index, value);
    }
}
