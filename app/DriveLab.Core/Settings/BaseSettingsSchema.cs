// ============================================================================
//  DriveLab
//  BaseSettingsSchema.cs — Schema (descritores) dos settings do volante: chave, faixa, unidade, aba e valor default.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

public static class BaseSettingsSchema
{
    public static IReadOnlyList<SettingDescriptor> All { get; } = new List<SettingDescriptor>
    {
        new(BaseSettingId.MotionRange, "motion_range", "Ângulo total de giro", SettingType.UInt16, 90, 2000, "°", SettingTab.Basic, 900),
        new(BaseSettingId.SoftStopRange, "soft_stop_range", "Range do batente", SettingType.UInt8, 0, 30, "°", SettingTab.Basic, 5),
        new(BaseSettingId.SoftStopStrength, "soft_stop_strength", "Força do batente", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 80),
        new(BaseSettingId.TotalStrength, "total_strength", "Força total", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 100),
        new(BaseSettingId.SpringStrength, "spring_strength", "Mola do volante", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 0),
        new(BaseSettingId.DamperStrength, "damper_strength", "Damper do volante", SettingType.UInt8, 0, 100, "%", SettingTab.Basic, 10),
        new(BaseSettingId.StaticDamping, "static_damping", "Damping estático", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 5),
        new(BaseSettingId.MaxTorqueLimit, "max_torque_limit", "Limite de torque", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 80),
        new(BaseSettingId.ForceDirection, "force_direction", "Direção da força", SettingType.Int8, -1, 1, "", SettingTab.Advanced, 1),
        new(BaseSettingId.EncoderDirection, "encoder_direction", "Direção do encoder", SettingType.Int8, -1, 1, "", SettingTab.Hardware, 1),
        new(BaseSettingId.EncoderCpr, "encoder_cpr", "CPR do encoder", SettingType.UInt16, 100, 60000, "contagens", SettingTab.Hardware, 10000),
        new(BaseSettingId.PolePairs, "pole_pairs", "Pares de polos", SettingType.UInt8, 1, 50, "", SettingTab.Hardware, 15),
        new(BaseSettingId.CurrentP, "current_p", "Ganho P (corrente)", SettingType.Float, 0, 10, "", SettingTab.Hardware, 0.05),
        new(BaseSettingId.CurrentI, "current_i", "Ganho I (corrente)", SettingType.Float, 0, 1000, "", SettingTab.Hardware, 10),
        new(BaseSettingId.CalibrationCurrent, "calibration_current", "Corrente de calibração", SettingType.UInt8, 0, 100, "%", SettingTab.Hardware, 30),
        new(BaseSettingId.PositionSmoothing, "position_smoothing", "Suavização de posição", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0),
        new(BaseSettingId.PowerLimit, "power_limit", "Limite de potência", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 100),
        new(BaseSettingId.BrakingLimit, "braking_limit", "Limite de frenagem", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 100),
        new(BaseSettingId.EncoderType, "encoder_type", "Tipo de encoder", SettingType.UInt8, 0, 1, "", SettingTab.Hardware, 0),
        new(BaseSettingId.ReconstructionSteps, "reconstruction_steps", "Reconstrução (passos, 0=auto)", SettingType.UInt8, 0, 32, "", SettingTab.Advanced, 0),
        new(BaseSettingId.ReconstructionLpf, "reconstruction_lpf", "Reconstrução (suavização)", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0),
        new(BaseSettingId.OutputFilterHz, "output_filter_hz", "Filtro de saída (corte)", SettingType.UInt16, 0, 2000, "Hz", SettingTab.Advanced, 0),
        new(BaseSettingId.OscGuardEnable, "osc_guard_enable", "Anti-oscilação", SettingType.UInt8, 0, 1, "", SettingTab.Advanced, 0),
        new(BaseSettingId.EndstopDamping, "endstop_damping", "Amortecimento do batente", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0),
        new(BaseSettingId.Linearity, "linearity", "Linearidade da resposta", SettingType.UInt8, 50, 200, "%", SettingTab.Advanced, 100),
        new(BaseSettingId.CoggingEnable, "cogging_enable", "Compensação de cogging", SettingType.UInt8, 0, 1, "", SettingTab.Advanced, 0),
        new(BaseSettingId.SlewRate, "slew_rate", "Limite de variação (slew)", SettingType.UInt8, 0, 100, "%", SettingTab.Advanced, 0),
        new(BaseSettingId.BusNominalV, "bus_nominal_v", "Tensão da fonte (nominal)", SettingType.UInt8, 12, 56, "V", SettingTab.Hardware, 56),
    };

    private static readonly Dictionary<byte, SettingDescriptor> ById =
        All.ToDictionary(d => (byte)d.Id);

    public static SettingDescriptor Get(BaseSettingId id) => ById[(byte)id];

    public static bool TryGet(byte fieldId, out SettingDescriptor descriptor) =>
        ById.TryGetValue(fieldId, out descriptor!);
}
