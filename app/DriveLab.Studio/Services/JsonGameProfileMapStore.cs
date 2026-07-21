// ============================================================================
//  DriveLab
//  JsonGameProfileMapStore.cs — Persiste o mapa de auto-perfil (jogo → perfis) em JSON no AppData.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.IO;
using System.Text.Json;
using DriveLab.Core.Games;

namespace DriveLab.Studio.Services;

/// <summary>Carrega/salva o <see cref="GameProfileMap"/> em <c>&lt;AppData&gt;/DriveLab/auto-profile.json</c>.
/// Tolera arquivo ausente/corrompido (retorna um mapa vazio).</summary>
public sealed class JsonGameProfileMapStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public JsonGameProfileMapStore(string? baseDir = null)
    {
        var root = baseDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DriveLab");
        Directory.CreateDirectory(root);
        _path = Path.Combine(root, "auto-profile.json");
    }

    public GameProfileMap Load()
    {
        try
        {
            if (!File.Exists(_path)) return new GameProfileMap();
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<GameProfileMap>(json, Options) ?? new GameProfileMap();
        }
        catch
        {
            return new GameProfileMap();
        }
    }

    public void Save(GameProfileMap map)
    {
        try { File.WriteAllText(_path, JsonSerializer.Serialize(map, Options)); }
        catch { /* melhor esforço: perder a persistência não deve derrubar o app */ }
    }
}
