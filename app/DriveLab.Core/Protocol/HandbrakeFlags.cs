namespace DriveLab.Core.Protocol;

/// <summary>Bits do campo Flags do PedalState quando usado pelo freio de mão.</summary>
[Flags]
public enum HandbrakeFlags : byte
{
    None = 0,
    ButtonPressed = 1,
}
