// ============================================================================
//  DriveLab
//  JsonHardwareProfileStore.cs — Lê/grava o perfil de hardware em ApplicationData/DriveLab/hardware-profile.json.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.IO;
using DriveLab.Core.Settings;

namespace DriveLab.Studio.Services;

/// <summary>Armazena o perfil de hardware na pasta do app. No macOS ApplicationData = <c>~/.config</c>,
/// então o caminho é <c>~/.config/DriveLab/hardware-profile.json</c> (Windows: %APPDATA%\DriveLab\...).</summary>
public sealed class JsonHardwareProfileStore
{
    public string Path { get; }

    public JsonHardwareProfileStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriveLab", "hardware-profile.json");
    }

    public bool Exists() => File.Exists(Path);

    /// <summary>Lê o perfil (ou null se não existe/ilegível). Não valida — quem chama valida via
    /// <see cref="HardwareProfileService.Validate"/>.</summary>
    public HardwareProfile? Load()
    {
        try
        {
            return File.Exists(Path) ? HardwareProfileService.Deserialize(File.ReadAllText(Path)) : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Save(HardwareProfile profile)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, HardwareProfileService.Serialize(profile));
    }
}
