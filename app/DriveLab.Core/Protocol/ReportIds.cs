namespace DriveLab.Core.Protocol;

public static class ReportIds
{
    public const byte DeviceState = 0x01;
    public const byte Command = 0x02;
    public const byte DirectControl = 0x10;
    public const byte SettingWrite = 0x14;
    public const byte SettingReadRequest = 0x15;
    public const byte SettingValue = 0x16;
}
