// ============================================================================
//  DriveLab
//  HardwareProfileService.cs — Monta / valida / (de)serializa o perfil de hardware. A fronteira "hardware"
//  vem do próprio schema (SettingTab.Hardware) — fonte única, sem lista hardcoded.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DriveLab.Core.Settings;

public static class HardwareProfileService
{
    public const int CurrentVersion = 1;
    public const string Kind = "hardware-profile";

    /// <summary>Os settings de HARDWARE = os da aba <see cref="SettingTab.Hardware"/> (fonte única).</summary>
    public static IReadOnlyList<SettingDescriptor> HardwareSettings { get; } =
        BaseSettingsSchema.All.Where(d => d.Tab == SettingTab.Hardware).ToList();

    public static bool IsHardware(BaseSettingId id) => HardwareSettings.Any(d => d.Id == id);

    /// <summary>Monta o perfil a partir dos valores atuais (getValue por id). createdAt é injetado
    /// (não usa DateTime.Now interno — testável/determinístico).</summary>
    public static HardwareProfile Build(string vendor, string device, string notes,
                                        DateTimeOffset createdAt, Func<BaseSettingId, double> getValue)
    {
        var p = new HardwareProfile
        {
            Version = CurrentVersion,
            Kind = Kind,
            Vendor = vendor ?? "",
            Device = device ?? "",
            Notes = notes ?? "",
            CreatedAt = createdAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
        };
        foreach (var d in HardwareSettings)
            p.Settings[d.Key] = getValue(d.Id);
        return p;
    }

    /// <summary>Valida o perfil contra o schema. Retorna a lista de problemas (vazia = OK). É a barreira de
    /// SEGURANÇA: nada é aplicado se houver valor fora de faixa / parâmetro desconhecido / kind errado.</summary>
    public static IReadOnlyList<string> Validate(HardwareProfile? p)
    {
        var issues = new List<string>();
        if (p is null) { issues.Add("perfil vazio ou ilegível"); return issues; }
        if (!string.Equals(p.Kind, Kind, StringComparison.Ordinal))
            issues.Add($"tipo inesperado: '{p.Kind}' (esperado '{Kind}')");
        if (p.Version > CurrentVersion)
            issues.Add($"versão {p.Version} mais nova que a suportada ({CurrentVersion})");
        if (p.Settings.Count == 0)
            issues.Add("perfil sem nenhum parâmetro");

        foreach (var kv in p.Settings)
        {
            var d = BaseSettingsSchema.All.FirstOrDefault(x => x.Key == kv.Key);
            if (d is null) { issues.Add($"parâmetro desconhecido: '{kv.Key}'"); continue; }
            if (d.Tab != SettingTab.Hardware) { issues.Add($"'{kv.Key}' não é de hardware"); continue; }
            if (kv.Value < d.Min || kv.Value > d.Max)
                issues.Add($"'{kv.Key}' = {kv.Value} fora da faixa [{d.Min}, {d.Max}]");
        }
        return issues;
    }

    /// <summary>Resolve o descritor de um parâmetro do perfil (ou null se desconhecido). Útil p/ a tela de
    /// confirmação mostrar rótulo/unidade.</summary>
    public static SettingDescriptor? DescriptorFor(string key) =>
        BaseSettingsSchema.All.FirstOrDefault(x => x.Key == key);

    private static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string Serialize(HardwareProfile p) => JsonSerializer.Serialize(p, Opt);

    public static HardwareProfile? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<HardwareProfile>(json, Opt); }
        catch (JsonException) { return null; }
    }
}
