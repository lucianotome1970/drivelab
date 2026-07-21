// ============================================================================
//  DriveLab
//  GitHubReleaseClient.cs — Consulta releases no GitHub e casa versão/asset por dispositivo (auto-update).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DriveLab.Core.Protocol;

namespace DriveLab.Core.Update;

public sealed record GitHubAsset(string Name, string DownloadUrl);
public sealed record GitHubRelease(string TagName, string Name, IReadOnlyList<GitHubAsset> Assets);

/// <summary>
/// Cliente de releases do GitHub: lista os releases do repositório e ajuda a achar a última versão e o asset
/// de cada dispositivo. O fetch de JSON é injetado (no app: HttpClient), então o parsing/comparação é puro e
/// host-testável com JSON canned — sem bater na rede nos testes. Tags por projeto (ex.: <c>firmware-base-v1.2.3</c>).
/// </summary>
public sealed class GitHubReleaseClient
{
    private readonly Func<Uri, Task<string>> _fetchJson;
    public string Owner { get; }
    public string Repo { get; }

    public GitHubReleaseClient(Func<Uri, Task<string>> fetchJson, string owner = "lucianotome1970", string repo = "drivelab")
    {
        _fetchJson = fetchJson;
        Owner = owner;
        Repo = repo;
    }

    /// <summary>Baixa e parseia os releases do repositório (mais novo primeiro, como a API do GitHub devolve).</summary>
    public async Task<IReadOnlyList<GitHubRelease>> ListReleasesAsync()
    {
        var uri = new Uri($"https://api.github.com/repos/{Owner}/{Repo}/releases");
        var json = await _fetchJson(uri).ConfigureAwait(false);
        return ParseReleases(json);
    }

    /// <summary>Parseia o JSON de releases da API do GitHub. Tolera campos ausentes.</summary>
    public static IReadOnlyList<GitHubRelease> ParseReleases(string json)
    {
        var list = new List<GitHubRelease>();
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return list;

        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            var tag = rel.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var name = rel.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var assets = new List<GitHubAsset>();
            if (rel.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var a in arr.EnumerateArray())
                    assets.Add(new GitHubAsset(
                        a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "",
                        a.TryGetProperty("browser_download_url", out var au) ? au.GetString() ?? "" : ""));
            list.Add(new GitHubRelease(tag, name, assets));
        }
        return list;
    }

    /// <summary>Prefixo de tag por dispositivo (ex.: Base → "firmware-base-v").</summary>
    public static string TagPrefixFor(DeviceKind kind) => kind switch
    {
        DeviceKind.Base => "firmware-base-v",
        DeviceKind.Pedal => "firmware-pedal-v",
        DeviceKind.Handbrake => "firmware-handbrake-v",
        DeviceKind.Wheel => "firmware-wheel-v",
        _ => "firmware-v",
    };

    /// <summary>Release de maior versão cujo tag começa com o prefixo, ou null.</summary>
    public static GitHubRelease? LatestFor(IEnumerable<GitHubRelease> releases, string prefix)
    {
        GitHubRelease? best = null;
        FirmwareVersion bestV = default;
        foreach (var r in releases)
        {
            if (!TryParseVersion(r.TagName, prefix, out var v)) continue;
            if (best is null || IsNewer(v, bestV)) { best = r; bestV = v; }
        }
        return best;
    }

    /// <summary>Extrai a versão "X.Y.Z" de um tag com o prefixo dado. Ignora sufixos após o patch.</summary>
    public static bool TryParseVersion(string tag, string prefix, out FirmwareVersion version)
    {
        version = default;
        if (string.IsNullOrEmpty(tag) || !tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var rest = tag.Substring(prefix.Length);
        var parts = rest.Split('.', '-', '+');
        if (parts.Length < 3) return false;
        if (!byte.TryParse(parts[0], out var maj) ||
            !byte.TryParse(parts[1], out var min) ||
            !byte.TryParse(parts[2], out var pat)) return false;
        version = new FirmwareVersion(0, maj, min, pat);
        return true;
    }

    /// <summary>True se `candidate` for mais nova que `installed` (compara Major.Minor.Patch).</summary>
    public static bool IsNewer(FirmwareVersion candidate, FirmwareVersion installed)
    {
        if (candidate.Major != installed.Major) return candidate.Major > installed.Major;
        if (candidate.Minor != installed.Minor) return candidate.Minor > installed.Minor;
        return candidate.Patch > installed.Patch;
    }

    /// <summary>Asset do release para o dispositivo: .bin para a base (STM32/DFU), .uf2 para os RP2040.</summary>
    public static GitHubAsset? AssetFor(GitHubRelease release, DeviceKind kind)
    {
        var ext = kind == DeviceKind.Base ? ".bin" : ".uf2";
        return release.Assets.FirstOrDefault(a => a.Name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }
}
