// ============================================================================
//  DriveLab
//  JsonNamedProfileStore.cs — Biblioteca de perfis nomeados em JSON (uma pasta por módulo, um arquivo por perfil).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DriveLab.Studio.Services;

/// <summary>Perfis nomeados em JSON: <c>&lt;AppData&gt;/DriveLab/profiles/&lt;module&gt;/&lt;nome&gt;.json</c>,
/// um arquivo por perfil. Enums gravados como texto (arquivo legível/estável).</summary>
public sealed class JsonNamedProfileStore<T> : INamedProfileStore<T> where T : class
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _dir;

    public JsonNamedProfileStore(string module, string? baseDir = null)
    {
        var root = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriveLab", "profiles");
        _dir = Path.Combine(root, module);
    }

    /// <summary>Sanitiza o nome para virar nome de arquivo seguro (troca chars inválidos por '_').</summary>
    private static string SafeFile(string name)
    {
        var cleaned = string.Concat(name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return cleaned.Length == 0 ? "_" : cleaned;
    }

    private string PathFor(string name) => Path.Combine(_dir, SafeFile(name) + ".json");

    public IReadOnlyList<string> ListNames()
    {
        if (!Directory.Exists(_dir))
            return System.Array.Empty<string>();
        return Directory.EnumerateFiles(_dir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, System.StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<T?> LoadAsync(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
            return null;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, Options);
    }

    public async Task SaveAsync(string name, T profile)
    {
        Directory.CreateDirectory(_dir);
        await using var stream = File.Create(PathFor(name));
        await JsonSerializer.SerializeAsync(stream, profile, Options);
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        if (File.Exists(path))
            File.Delete(path);
    }

    public void Rename(string oldName, string newName)
    {
        var from = PathFor(oldName);
        if (!File.Exists(from))
            return;
        var to = PathFor(newName);
        if (File.Exists(to))
            File.Delete(to);
        File.Move(from, to);
    }
}
