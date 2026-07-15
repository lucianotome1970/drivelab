// ============================================================================
//  DriveLab
//  JsonHandbrakeProfileStorage.cs — Persiste o perfil do freio de mão em JSON local, em caminho fixo do usuário.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.IO;
using System.Text.Json;

namespace DriveLab.Studio.Services;

/// <summary>Perfil local em JSON num caminho fixo do usuário (espelha JsonPedalProfileStorage).</summary>
public sealed class JsonHandbrakeProfileStorage : IHandbrakeProfileStorage
{
    private readonly string _path;

    public JsonHandbrakeProfileStorage(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriveLab", "handbrake.json");
    }

    public async Task SaveAsync(HandbrakeProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, profile);
    }

    public async Task<HandbrakeProfile?> LoadAsync()
    {
        if (!File.Exists(_path))
            return null;
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<HandbrakeProfile>(stream);
    }
}
