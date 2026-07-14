namespace DriveLab.Studio.Services;

public sealed record PedalProfileColumn(
    int Sensor, int InputMin, int InputMax, bool Invert, int Smooth, double[] Curve, int LoadCellScale,
    int LoadCellMaxKg = 100, bool BrakeUnitKg = false);

public sealed record PedalProfile(PedalProfileColumn[] Columns);

public interface IPedalProfileStorage
{
    Task SaveAsync(PedalProfile profile);
    Task<PedalProfile?> LoadAsync();
}
