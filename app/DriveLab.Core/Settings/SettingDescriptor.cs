// ============================================================================
//  DriveLab
//  SettingDescriptor.cs — Descritor de um setting do volante: chave, faixa, unidade, aba e valor default.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Settings;

public sealed record SettingDescriptor(
    SettingId Id,
    string Key,
    string DisplayName,
    SettingType Type,
    double Min,
    double Max,
    string Unit,
    SettingTab Tab,
    double Default)
{
    public double Clamp(double value) => Math.Clamp(value, Min, Max);
}
