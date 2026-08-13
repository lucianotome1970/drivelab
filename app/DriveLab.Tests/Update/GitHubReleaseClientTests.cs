// ============================================================================
//  DriveLab
//  GitHubReleaseClientTests.cs — Testes do parsing de releases, comparação de versão e match de asset.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Threading.Tasks;
using DriveLab.Core.Protocol;
using DriveLab.Core.Update;

namespace DriveLab.Tests.Update;

public class GitHubReleaseClientTests
{
    private const string Json = """
    [
      {"tag_name":"firmware-base-v0.3.0","name":"Base 0.3.0","assets":[
        {"name":"firmware-base-0.3.0.bin","browser_download_url":"https://x/base.bin"}]},
      {"tag_name":"firmware-base-v0.2.0","name":"Base 0.2.0","assets":[]},
      {"tag_name":"firmware-pedal-v1.0.0","name":"Pedal","assets":[
        {"name":"firmware-pedal.uf2","browser_download_url":"https://x/pedal.uf2"}]},
      {"tag_name":"studio-v2.0.0","name":"Studio","assets":[]}
    ]
    """;

    [Fact]
    public void ParseReleases_ReadsTagsAndAssets()
    {
        var rels = GitHubReleaseClient.ParseReleases(Json);
        Assert.Equal(4, rels.Count);
        Assert.Equal("firmware-base-v0.3.0", rels[0].TagName);
        Assert.Single(rels[0].Assets);
        Assert.Equal("https://x/base.bin", rels[0].Assets[0].DownloadUrl);
    }

    [Fact]
    public void LatestFor_PicksHighestVersion()
    {
        var rels = GitHubReleaseClient.ParseReleases(Json);
        var latest = GitHubReleaseClient.LatestFor(rels, "firmware-base-v");
        Assert.NotNull(latest);
        Assert.Equal("firmware-base-v0.3.0", latest!.TagName);
    }

    [Fact]
    public void TryParseVersion_ExtractsSemver()
    {
        Assert.True(GitHubReleaseClient.TryParseVersion("firmware-base-v0.3.0", "firmware-base-v", out var v));
        Assert.Equal((byte)0, v.Major);
        Assert.Equal((byte)3, v.Minor);
        Assert.Equal((byte)0, v.Patch);

        Assert.False(GitHubReleaseClient.TryParseVersion("studio-v2.0.0", "firmware-base-v", out _));
    }

    [Fact]
    public void IsNewer_ComparesMajorMinorPatch()
    {
        Assert.True(GitHubReleaseClient.IsNewer(new(0, 0, 3, 0), new(0, 0, 2, 0)));
        Assert.False(GitHubReleaseClient.IsNewer(new(0, 0, 2, 0), new(0, 0, 3, 0)));
        Assert.False(GitHubReleaseClient.IsNewer(new(0, 0, 3, 0), new(0, 0, 3, 0)));
        Assert.True(GitHubReleaseClient.IsNewer(new(0, 1, 0, 0), new(0, 0, 9, 9)));
    }

    [Fact]
    public void AssetFor_PicksBinForBase_Uf2ForRp2040()
    {
        var rels = GitHubReleaseClient.ParseReleases(Json);
        var baseRel = GitHubReleaseClient.LatestFor(rels, "firmware-base-v")!;
        var pedalRel = GitHubReleaseClient.LatestFor(rels, "firmware-pedal-v")!;

        Assert.EndsWith(".bin", GitHubReleaseClient.AssetFor(baseRel, DeviceKind.Base)!.Name);
        Assert.EndsWith(".uf2", GitHubReleaseClient.AssetFor(pedalRel, DeviceKind.Pedal)!.Name);
        Assert.Null(GitHubReleaseClient.AssetFor(pedalRel, DeviceKind.Base));   // pedal não tem .bin
    }

    [Fact]
    public async Task ListReleasesAsync_UsesInjectedFetch()
    {
        Uri? seen = null;
        var client = new GitHubReleaseClient(uri => { seen = uri; return Task.FromResult(Json); });
        var rels = await client.ListReleasesAsync();

        Assert.Equal(4, rels.Count);
        Assert.Contains("lucianotome1970/drivelab", seen!.ToString());
    }
}
