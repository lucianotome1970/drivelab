using DriveLab.Core.Settings;

namespace DriveLab.Studio.ViewModels;

/// <summary>Opções (enum) para settings que devem virar chips rotulados, análogo a SettingPresets.</summary>
public static class SettingOptions
{
    public sealed record EnumOptionSpec(int Value, string LabelKey);

    private static readonly IReadOnlyList<EnumOptionSpec> Empty = Array.Empty<EnumOptionSpec>();

    private static readonly Dictionary<SettingId, IReadOnlyList<EnumOptionSpec>> Map = new()
    {
        [SettingId.EncoderType] = new[]
        {
            new EnumOptionSpec(0, "Setting_EncoderType_Quadrature"),
            new EnumOptionSpec(1, "Setting_EncoderType_MagneticSPI"),
        },
    };

    public static IReadOnlyList<EnumOptionSpec> For(SettingId id) =>
        Map.TryGetValue(id, out var v) ? v : Empty;
}
