namespace DriveLab.Core.Settings;

public sealed record HandbrakeSettingDescriptor(
    HandbrakeSettingId Id,
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

public static class HandbrakeSettingsSchema
{
    public static IReadOnlyList<HandbrakeSettingDescriptor> All { get; } = new List<HandbrakeSettingDescriptor>
    {
        new(HandbrakeSettingId.SensorType, "sensor_type", "Tipo de sensor", SettingType.UInt8, 0, 2, "", 0),
        new(HandbrakeSettingId.InputMin, "input_min", "Mínimo (calibração)", SettingType.UInt16, 0, 65535, "", 0),
        new(HandbrakeSettingId.InputMax, "input_max", "Máximo (calibração)", SettingType.UInt16, 0, 65535, "", 4095),
        new(HandbrakeSettingId.Invert, "invert", "Inverter", SettingType.UInt8, 0, 1, "", 0),
        new(HandbrakeSettingId.Smooth, "smooth", "Suavização", SettingType.UInt8, 0, 100, "%", 0),
        new(HandbrakeSettingId.CurvePoint0, "curve_point_0", "Ponto 0%", SettingType.UInt8, 0, 100, "%", 0),
        new(HandbrakeSettingId.CurvePoint1, "curve_point_1", "Ponto 20%", SettingType.UInt8, 0, 100, "%", 20),
        new(HandbrakeSettingId.CurvePoint2, "curve_point_2", "Ponto 40%", SettingType.UInt8, 0, 100, "%", 40),
        new(HandbrakeSettingId.CurvePoint3, "curve_point_3", "Ponto 60%", SettingType.UInt8, 0, 100, "%", 60),
        new(HandbrakeSettingId.CurvePoint4, "curve_point_4", "Ponto 80%", SettingType.UInt8, 0, 100, "%", 80),
        new(HandbrakeSettingId.CurvePoint5, "curve_point_5", "Ponto 100%", SettingType.UInt8, 0, 100, "%", 100),
        new(HandbrakeSettingId.LoadCellScale, "loadcell_scale", "Escala load cell", SettingType.UInt16, 1, 65535, "", 1000),
        new(HandbrakeSettingId.DeadzoneLow, "deadzone_low", "Deadzone baixo", SettingType.UInt8, 0, 100, "%", 0),
        new(HandbrakeSettingId.DeadzoneHigh, "deadzone_high", "Deadzone alto", SettingType.UInt8, 0, 100, "%", 100),
        new(HandbrakeSettingId.ButtonThreshold, "button_threshold", "Limiar do botão", SettingType.UInt8, 0, 100, "%", 70),
        new(HandbrakeSettingId.ButtonEnabled, "button_enabled", "Botão digital", SettingType.UInt8, 0, 1, "", 1),
    };

    public static HandbrakeSettingId[] CurvePointIds { get; } =
    {
        HandbrakeSettingId.CurvePoint0, HandbrakeSettingId.CurvePoint1, HandbrakeSettingId.CurvePoint2,
        HandbrakeSettingId.CurvePoint3, HandbrakeSettingId.CurvePoint4, HandbrakeSettingId.CurvePoint5,
    };

    private static readonly Dictionary<byte, HandbrakeSettingDescriptor> ById =
        All.ToDictionary(d => (byte)d.Id);

    public static HandbrakeSettingDescriptor Get(HandbrakeSettingId id) => ById[(byte)id];

    public static bool TryGet(byte fieldId, out HandbrakeSettingDescriptor descriptor) =>
        ById.TryGetValue(fieldId, out descriptor!);
}
