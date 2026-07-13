namespace DriveLab.Core.Protocol;

[Flags]
public enum DeviceFlags : byte
{
    None = 0,
    ForceEnabled = 1,
    Calibrated = 2,
    Error = 4,
    UsingSimulator = 8,
}
