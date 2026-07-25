// ============================================================================
//  DriveLab
//  HardwareProfile.cs — Perfil de HARDWARE (variante, motor, encoder, corrente…) que um construtor de DD
//  define e distribui num .json junto com o produto; o app do comprador detecta, valida e aplica.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.Generic;

namespace DriveLab.Core.Settings;

/// <summary>Perfil de hardware serializável (JSON). Cobre só os settings <see cref="SettingTab.Hardware"/>
/// — a camada perigosa/fixa — separada do perfil de "feel" do usuário. As chaves de <see cref="Settings"/>
/// são os <c>Key</c> do schema (ex.: "board_variant").</summary>
public sealed class HardwareProfile
{
    public int Version { get; set; } = HardwareProfileService.CurrentVersion;
    public string Kind { get; set; } = HardwareProfileService.Kind;
    public string Vendor { get; set; } = "";
    public string Device { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string Notes { get; set; } = "";
    public Dictionary<string, double> Settings { get; set; } = new();
}
