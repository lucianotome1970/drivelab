using DriveLab.Core.Settings;

namespace DriveLab.Studio.ViewModels;

/// <summary>Presets de valor rápido por setting (chips abaixo do slider, estilo MOZA).</summary>
public static class SettingPresets
{
    private static readonly Dictionary<SettingId, int[]> Map = new()
    {
        [SettingId.MotionRange] = new[] { 360, 540, 720, 900, 1080, 1440 },
        [SettingId.TotalStrength] = new[] { 30, 50, 70, 100 },
        [SettingId.MaxTorqueLimit] = new[] { 50, 80, 100 },
        [SettingId.SoftStopStrength] = new[] { 0, 50, 80, 100 },
        [SettingId.SoftStopRange] = new[] { 0, 5, 10 },
        [SettingId.SpringStrength] = new[] { 0, 25, 50, 100 },
        [SettingId.DamperStrength] = new[] { 0, 30, 60 },
        [SettingId.StaticDamping] = new[] { 0, 5, 30 },
        [SettingId.PositionSmoothing] = new[] { 0, 25, 50 },
        [SettingId.PowerLimit] = new[] { 50, 80, 100 },
        [SettingId.BrakingLimit] = new[] { 50, 80, 100 },
    };

    public static IReadOnlyList<int> For(SettingId id) =>
        Map.TryGetValue(id, out var presets) ? presets : Array.Empty<int>();
}
