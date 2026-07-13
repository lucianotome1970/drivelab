namespace DriveLab.Core.Protocol;

public enum PedalCommandId : byte
{
    CalibrateStart = 1,
    CalibrateStop = 2,
    SaveToFlash = 3,
    LoadDefaults = 4,
}
