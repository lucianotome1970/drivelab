// ============================================================================
//  DriveLab
//  JsonHardwareProfileStore.cs — Lê/grava o perfil de hardware. Precedência: o perfil INSTALADO junto do app
//  (ao lado do .exe, empacotado pelo instalador do criador) vence o de ApplicationData (dev/manual).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.IO;
using DriveLab.Core.Settings;

namespace DriveLab.Studio.Services;

/// <summary>Resolve o perfil de hardware de dois lugares, nesta ordem:
/// 1) <b>bundled</b> — ao lado do executável (<c>AppContext.BaseDirectory</c>): é o que o INSTALADOR do
///    criador empacota junto (ex.: <c>C:\Program Files\DriveLab\hardware-profile.json</c>). Read-only ok.
/// 2) <b>AppData</b> — <c>ApplicationData/DriveLab/hardware-profile.json</c> (dev / ajuste manual do usuário).
/// O bundled vence (é a config oficial do criador). Save() sempre grava no AppData (gravável).</summary>
public sealed class JsonHardwareProfileStore
{
    public string BundledPath { get; }
    public string AppDataPath { get; }

    public JsonHardwareProfileStore(string? bundledPath = null, string? appDataPath = null)
    {
        BundledPath = bundledPath ?? System.IO.Path.Combine(
            AppContext.BaseDirectory, "hardware-profile.json");
        AppDataPath = appDataPath ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriveLab", "hardware-profile.json");
    }

    /// <summary>Caminho efetivo (bundled tem precedência); null se nenhum existe.</summary>
    public string? EffectivePath =>
        File.Exists(BundledPath) ? BundledPath
        : File.Exists(AppDataPath) ? AppDataPath
        : null;

    public bool Exists() => EffectivePath is not null;

    /// <summary>Lê o perfil do caminho efetivo (ou null). Não valida — quem chama valida.</summary>
    public HardwareProfile? Load()
    {
        var path = EffectivePath;
        if (path is null) return null;
        try { return HardwareProfileService.Deserialize(File.ReadAllText(path)); }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Grava no AppData (gravável). O bundled é read-only (instalado em Program Files).</summary>
    public void Save(HardwareProfile profile)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(AppDataPath)!);
        File.WriteAllText(AppDataPath, HardwareProfileService.Serialize(profile));
    }
}
