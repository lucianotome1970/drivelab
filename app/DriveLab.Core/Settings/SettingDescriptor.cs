namespace DriveLab.Core.Settings;

public sealed record SettingDescriptor(
    SettingId Id,
    string Key,
    string DisplayName,
    SettingType Type,
    double Min,
    double Max,
    string Unit,
    SettingTab Tab,
    double Default)
{
    public double Clamp(double value) => Math.Clamp(value, Min, Max);
}
