using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Services;

public sealed record WheelButtonColor(string Name, string ColorHex);

public sealed record WheelProfile(
    WheelButtonColor[] Buttons,
    int PaddleCount,
    PaddleFunction BottomFunction,
    PaddleMode BottomMode,
    PaddleActuation BottomActuation,
    int BottomBitePoint);

public interface IWheelProfileStorage
{
    Task SaveAsync(WheelProfile profile);
    Task<WheelProfile?> LoadAsync();
}
