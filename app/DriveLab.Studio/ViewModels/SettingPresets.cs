// ============================================================================
//  DriveLab
//  SettingPresets.cs — Presets de valor rápido por BaseSettingId, exibidos como chips estilo MOZA.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;

namespace DriveLab.Studio.ViewModels;

/// <summary>Presets de valor rápido por setting (chips abaixo do slider, estilo MOZA).</summary>
public static class SettingPresets
{
    private static readonly Dictionary<BaseSettingId, int[]> Map = new()
    {
        // ⚠️ PARA EM 1080, e o limite é do PROTOCOLO, não de gosto: o eixo que vai para o jogo
        // representa ±540° (±32767 sobre 1,5 volta). Num curso de 1440 o volante passaria do que o
        // relatório consegue dizer, e o jogo veria o eixo travado no extremo enquanto o aro continua
        // girando — pior que não ter a opção.
        [BaseSettingId.MotionRange] = new[] { 360, 540, 720, 900, 1080 },
        [BaseSettingId.TotalStrength] = new[] { 30, 50, 70, 100 },
        [BaseSettingId.MaxTorqueLimit] = new[] { 50, 80, 100 },
        [BaseSettingId.SoftStopStrength] = new[] { 0, 50, 80, 100 },
        [BaseSettingId.SoftStopRange] = new[] { 0, 5, 10 },
        [BaseSettingId.SpringStrength] = new[] { 0, 25, 50, 100 },
        [BaseSettingId.DamperStrength] = new[] { 0, 30, 60 },
        [BaseSettingId.StaticDamping] = new[] { 0, 5, 30 },
        [BaseSettingId.PositionSmoothing] = new[] { 0, 25, 50 },
        [BaseSettingId.PowerLimit] = new[] { 50, 80, 100 },
        [BaseSettingId.BrakingLimit] = new[] { 50, 80, 100 },
    };

    public static IReadOnlyList<int> For(BaseSettingId id) =>
        Map.TryGetValue(id, out var presets) ? presets : Array.Empty<int>();
}
