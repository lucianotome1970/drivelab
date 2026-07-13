namespace DriveLab.Core.Transport;

public enum DeviceCommand : byte
{
    Reboot = 1,
    SaveSettings = 2,
    ResetCenter = 3,
    EnterDfu = 4,
    Calibrate = 5,
    SetForceEnabled = 6,
}
