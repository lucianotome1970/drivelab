namespace DriveLab.Core.Protocol;

public static class PedalReportIds
{
    public const byte PedalState = 0x20;
    public const byte Command = 0x02;
    public const byte SettingWrite = 0x14;
    public const byte SettingReadRequest = 0x15;
    public const byte SettingValue = 0x16;
}
