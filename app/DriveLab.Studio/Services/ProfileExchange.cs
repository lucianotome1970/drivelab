// ============================================================================
//  DriveLab
//  ProfileExchange.cs — Envelope versionado para exportar/importar perfis (compartilhar na comunidade).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DriveLab.Studio.Services;

/// <summary>Um perfil dentro do arquivo exportado. O NOME viaja junto; o id/arquivo é do lado de quem importa.</summary>
public sealed class ProfileExportEntry<T>
{
    public string Name { get; set; } = "";
    public T? Data { get; set; }
}

/// <summary>Arquivo de perfis exportados. <see cref="Version"/> permite evoluir o formato sem quebrar
/// arquivos antigos; <see cref="Module"/> evita importar perfil de pedal na tela da base.</summary>
public sealed class ProfileExportEnvelope<T>
{
    public int Version { get; set; } = ProfileExchange.CurrentVersion;
    public string ExportedAt { get; set; } = "";
    public string Module { get; set; } = "";
    public List<ProfileExportEntry<T>> Profiles { get; set; } = new();
}

/// <summary>
/// Serialização dos perfis para arquivo e de volta. Puro (sem I/O) → host-testável. Os perfis são
/// identificados só pelo NOME; ao importar, nomes que já existem ganham sufixo em vez de sobrescrever.
/// </summary>
public static class ProfileExchange
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(string module, IEnumerable<(string Name, T Data)> profiles, DateTimeOffset exportedAt)
    {
        var envelope = new ProfileExportEnvelope<T>
        {
            Version = CurrentVersion,
            ExportedAt = exportedAt.ToString("o"),
            Module = module,
            Profiles = profiles.Select(p => new ProfileExportEntry<T> { Name = p.Name, Data = p.Data }).ToList(),
        };
        return JsonSerializer.Serialize(envelope, Options);
    }

    /// <summary>Lê o arquivo. Lança <see cref="InvalidOperationException"/> com mensagem amigável se o JSON não
    /// for um envelope válido ou for de uma versão futura.</summary>
    public static ProfileExportEnvelope<T> Deserialize<T>(string json)
    {
        ProfileExportEnvelope<T>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ProfileExportEnvelope<T>>(json, Options);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Arquivo inválido: não é um JSON de perfis do DriveLab.");
        }

        if (envelope is null || envelope.Profiles is null)
            throw new InvalidOperationException("Arquivo inválido: nenhum perfil encontrado.");
        if (envelope.Version > CurrentVersion)
            throw new InvalidOperationException(
                $"Arquivo de uma versão mais nova (v{envelope.Version}). Atualize o DriveLab Studio.");

        return envelope;
    }

    /// <summary>Nome livre: se já existir, vira "nome (2)", "nome (3)"… (não sobrescreve o perfil do usuário).</summary>
    public static string UniqueName(IEnumerable<string> existing, string desired)
    {
        var taken = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(desired)) desired = "Importado";
        if (!taken.Contains(desired)) return desired;

        for (var i = 2; ; i++)
        {
            var candidate = $"{desired} ({i})";
            if (!taken.Contains(candidate)) return candidate;
        }
    }
}
