namespace DriveLab.Core.Settings;

public static class SettingsSchema
{
    public static IReadOnlyList<SettingDescriptor> All { get; } = new List<SettingDescriptor>
    {
        new(SettingId.MotionRange, "motion_range", "Ângulo total de giro", SettingType.UInt16, 90, 2000, "°", SettingTab.Basic, 900),
        new(SettingId.SoftStopRange, "soft_stop_range", "Range do batente", SettingType.UInt8, 0, 30, "°", SettingTab.Basic, 5),
        new(SettingId.SoftStopStrength, "soft_stop_strength", "Força do batente", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 80),
        new(SettingId.TotalStrength, "total_strength", "Força total", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 100),
        new(SettingId.SpringStrength, "spring_strength", "Mola do volante", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 0),
        new(SettingId.DamperStrength, "damper_strength", "Damper do volante", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 10),
        new(SettingId.StaticDamping, "static_damping", "Damping estático", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 5),
        new(SettingId.MaxTorqueLimit, "max_torque_limit", "Limite de torque", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 80),
        new(SettingId.ForceDirection, "force_direction", "Direção da força", SettingType.Int8, -1, 1, "", SettingTab.Advanced, 1),
        new(SettingId.EncoderDirection, "encoder_direction", "Direção do encoder", SettingType.Int8, -1, 1, "", SettingTab.Hardware, 1),
        new(SettingId.EncoderCpr, "encoder_cpr", "CPR do encoder", SettingType.UInt16, 100, 60000, "contagens", SettingTab.Hardware, 10000),
        new(SettingId.PolePairs, "pole_pairs", "Pares de polos", SettingType.UInt8, 1, 50, "", SettingTab.Hardware, 15),
        new(SettingId.CurrentP, "current_p", "Ganho P (corrente)", SettingType.Float, 0, 10, "", SettingTab.Hardware, 0.05),
        new(SettingId.CurrentI, "current_i", "Ganho I (corrente)", SettingType.Float, 0, 1000, "", SettingTab.Hardware, 10),
        new(SettingId.CalibrationCurrent, "calibration_current", "Corrente de calibração", SettingType.UInt8, 0, 100, "%", SettingTab.Hardware, 30),
        new(SettingId.PositionSmoothing, "position_smoothing", "Suavização de posição", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0),
        new(SettingId.PowerLimit, "power_limit", "Limite de potência", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 100),
        new(SettingId.BrakingLimit, "braking_limit", "Limite de frenagem", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 100),
        new(SettingId.EncoderType, "encoder_type", "Tipo de encoder", SettingType.UInt8, 0, 1, "", SettingTab.Hardware, 0),
    };

    private static readonly Dictionary<byte, SettingDescriptor> ById =
        All.ToDictionary(d => (byte)d.Id);

    public static SettingDescriptor Get(SettingId id) => ById[(byte)id];

    public static bool TryGet(byte fieldId, out SettingDescriptor descriptor) =>
        ById.TryGetValue(fieldId, out descriptor!);
}
