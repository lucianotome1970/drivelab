namespace DriveLab.Studio.ViewModels;

public sealed record PedalCurvePreset(string Name, double[] Points);

public static class PedalCurvePresets
{
    public static IReadOnlyList<PedalCurvePreset> All { get; } = new List<PedalCurvePreset>
    {
        new("Linear", new double[] { 0, 20, 40, 60, 80, 100 }),
        new("S-Curve", new double[] { 0, 8, 28, 72, 92, 100 }),
        new("Fast", new double[] { 0, 38, 62, 78, 90, 100 }),
        new("Slow", new double[] { 0, 10, 22, 38, 62, 100 }),
    };
}
