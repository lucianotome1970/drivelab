// ============================================================================
//  DriveLab
//  JsonWheelProfileStorageTests.cs — Testes de round-trip do JsonWheelProfileStorage.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.IO;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.Services;

public class JsonWheelProfileStorageTests
{
    [Fact]
    public async Task Save_Then_Load_Roundtrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wheel-{Guid.NewGuid():N}.json");
        try
        {
            var storage = new JsonWheelProfileStorage(path);
            var profile = new WheelProfile(
                new[] { new WheelButtonColor("N", "#BF5AF2"), new WheelButtonColor("PIT", "#FFD60A") },
                PaddleCount: 4,
                BottomFunction: PaddleFunction.Clutch, BottomMode: PaddleMode.Independent,
                BottomActuation: PaddleActuation.Progression, BottomBitePoint: 65);
            await storage.SaveAsync(profile);
            var loaded = await storage.LoadAsync();

            Assert.NotNull(loaded);
            Assert.Equal(2, loaded!.Buttons.Length);
            Assert.Equal("#FFD60A", loaded.Buttons[1].ColorHex);
            Assert.Equal(4, loaded.PaddleCount);
            Assert.Equal(PaddleMode.Independent, loaded.BottomMode);
            Assert.Equal(PaddleActuation.Progression, loaded.BottomActuation);
            Assert.Equal(65, loaded.BottomBitePoint);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Load_Returns_Null_When_Missing()
    {
        var storage = new JsonWheelProfileStorage(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
        Assert.Null(await storage.LoadAsync());
    }
}
