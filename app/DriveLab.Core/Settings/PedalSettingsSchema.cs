namespace DriveLab.Core.Settings;

public sealed record PedalSettingDescriptor(
    PedalSettingId Id,
    string Key,
    string DisplayName,
    SettingType Type,
    double Min,
    double Max,
    string Unit,
    double Default)
{
    public double Clamp(double value) => Math.Clamp(value, Min, Max);
}

public static class PedalSettingsSchema
{
    public static IReadOnlyList<PedalSettingDescriptor> All { get; } = new List<PedalSettingDescriptor>
    {
        new(PedalSettingId.SensorType, "sensor_type", "Tipo de sensor", SettingType.UInt8, 0, 2, "", 0),
        new(PedalSettingId.InputMin, "input_min", "Mínimo (calibração)", SettingType.UInt16, 0, 65535, "", 0),
        new(PedalSettingId.InputMax, "input_max", "Máximo (calibração)", SettingType.UInt16, 0, 65535, "", 4095),
        new(PedalSettingId.Invert, "invert", "Inverter", SettingType.UInt8, 0, 1, "", 0),
        new(PedalSettingId.Smooth, "smooth", "Suavização", SettingType.UInt8, 0, 100, "%", 0),
        new(PedalSettingId.CurvePoint0, "curve_point_0", "Ponto 0%", SettingType.UInt8, 0, 100, "%", 0),
        new(PedalSettingId.CurvePoint1, "curve_point_1", "Ponto 20%", SettingType.UInt8, 0, 100, "%", 20),
        new(PedalSettingId.CurvePoint2, "curve_point_2", "Ponto 40%", SettingType.UInt8, 0, 100, "%", 40),
        new(PedalSettingId.CurvePoint3, "curve_point_3", "Ponto 60%", SettingType.UInt8, 0, 100, "%", 60),
        new(PedalSettingId.CurvePoint4, "curve_point_4", "Ponto 80%", SettingType.UInt8, 0, 100, "%", 80),
        new(PedalSettingId.CurvePoint5, "curve_point_5", "Ponto 100%", SettingType.UInt8, 0, 100, "%", 100),
        new(PedalSettingId.LoadCellScale, "loadcell_scale", "Escala load cell", SettingType.UInt16, 1, 65535, "", 1000),
        new(PedalSettingId.DeadzoneLow, "deadzone_low", "Deadzone baixo", SettingType.UInt8, 0, 100, "%", 0),
        new(PedalSettingId.DeadzoneHigh, "deadzone_high", "Deadzone alto", SettingType.UInt8, 0, 100, "%", 100),
    };

    public static PedalSettingId[] CurvePointIds { get; } =
    {
        PedalSettingId.CurvePoint0, PedalSettingId.CurvePoint1, PedalSettingId.CurvePoint2,
        PedalSettingId.CurvePoint3, PedalSettingId.CurvePoint4, PedalSettingId.CurvePoint5,
    };

    private static readonly Dictionary<byte, PedalSettingDescriptor> ById =
        All.ToDictionary(d => (byte)d.Id);

    public static PedalSettingDescriptor Get(PedalSettingId id) => ById[(byte)id];

    public static bool TryGet(byte fieldId, out PedalSettingDescriptor descriptor) =>
        ById.TryGetValue(fieldId, out descriptor!);
}
