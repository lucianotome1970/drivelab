// ============================================================================
//  DriveLab
//  CenterButtonStore.cs — Persiste (JSON) a LISTA de mapeamentos do atalho de centralizar (vários ao mesmo
//    tempo, estilo ACC). Migra dos formatos anteriores: item único {Kind,Mask,Keys/Key} → lista de 1; e o
//    formato bem antigo "só do aro" ({"Mask":N}) → lista vazia (aquele modelo não existe mais).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DriveLab.Studio.Services;

/// <summary>Guarda a lista de <see cref="CenterBinding"/>. Falhas de I/O são engolidas (feature opcional;
/// nunca derruba o app).</summary>
public sealed class CenterButtonStore
{
    private readonly string _path;

    public CenterButtonStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriveLab", "center-button.json");
    }

    public IReadOnlyList<CenterBinding> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var file = JsonSerializer.Deserialize<Data>(json);
                if (file?.Bindings is { Count: > 0 })
                    return file.Bindings.Select(FromItem).OfType<CenterBinding>().ToList();

                // Formatos anteriores: um único item no topo do JSON.
                var one = JsonSerializer.Deserialize<Item>(json);
                var b = one is null ? null : FromItem(one);
                return b is null ? Array.Empty<CenterBinding>() : new[] { b };
            }
        }
        catch { /* ausente/corrompido → sem mapeamentos */ }
        return Array.Empty<CenterBinding>();
    }

    public void Save(IReadOnlyList<CenterBinding> bindings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var data = new Data
            {
                Bindings = bindings.Select(b => new Item
                {
                    Kind = (int)b.Kind,
                    DeviceId = b.DeviceId,
                    DeviceName = b.DeviceName,
                    Mask = b.Mask,
                    Keys = b.Keys.Count > 0 ? b.Keys.ToArray() : null,
                }).ToList(),
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(data));
        }
        catch { /* melhor esforço; não derruba o app */ }
    }

    // Só Hid/Keyboard válidos viram binding; o Kind ausente (formato "só do aro") vira null → descartado.
    private static CenterBinding? FromItem(Item it)
    {
        if (it.Kind != (int)CenterSourceKind.Hid && it.Kind != (int)CenterSourceKind.Keyboard)
            return null;
        var keys = it.Keys ?? (it.Key != 0 ? new[] { it.Key } : null);   // legado: Key único → combo de 1
        return new CenterBinding((CenterSourceKind)it.Kind, it.DeviceId, it.Mask, keys, it.DeviceName);
    }

    private sealed class Data
    {
        public List<Item>? Bindings { get; set; }
    }

    private sealed class Item
    {
        public int Kind { get; set; }
        public string? DeviceId { get; set; }
        public string? DeviceName { get; set; }
        public uint Mask { get; set; }
        public int Key { get; set; }           // legado (fase C): tecla única
        public int[]? Keys { get; set; }
    }
}
