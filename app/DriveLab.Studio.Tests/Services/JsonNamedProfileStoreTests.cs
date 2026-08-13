// ============================================================================
//  DriveLab
//  JsonNamedProfileStoreTests.cs — Testes da biblioteca de perfis nomeados (CRUD + listagem).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.IO;
using System.Threading.Tasks;
using DriveLab.Studio.Services;
using Xunit;

namespace DriveLab.Studio.Tests.Services;

public class JsonNamedProfileStoreTests
{
    private sealed record Sample(string Label, int Value);

    private static (JsonNamedProfileStore<Sample> store, string dir) New()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dlprofiles-{Guid.NewGuid():N}");
        return (new JsonNamedProfileStore<Sample>("wheel", dir), dir);
    }

    [Fact]
    public async Task Save_List_Load_Roundtrips()
    {
        var (store, dir) = New();
        try
        {
            Assert.Empty(store.ListNames());
            await store.SaveAsync("GT3", new Sample("gt3", 7));
            await store.SaveAsync("Chuva", new Sample("rain", 3));

            Assert.Equal(new[] { "Chuva", "GT3" }, store.ListNames());  // ordem alfabética
            var gt3 = await store.LoadAsync("GT3");
            Assert.Equal("gt3", gt3!.Label);
            Assert.Equal(7, gt3.Value);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Delete_And_Rename_Work()
    {
        var (store, dir) = New();
        try
        {
            await store.SaveAsync("A", new Sample("a", 1));
            await store.SaveAsync("B", new Sample("b", 2));

            store.Rename("A", "Rally");
            Assert.Equal(new[] { "B", "Rally" }, store.ListNames());
            Assert.NotNull(await store.LoadAsync("Rally"));
            Assert.Null(await store.LoadAsync("A"));

            store.Delete("B");
            Assert.Equal(new[] { "Rally" }, store.ListNames());
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Load_Missing_Returns_Null()
    {
        var (store, _) = New();
        Assert.Null(await store.LoadAsync("nope"));
    }
}
