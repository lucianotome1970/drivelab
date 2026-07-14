using System.IO;
using DriveLab.Studio.Services;
using Xunit;

namespace DriveLab.Studio.Tests;

public class JsonHandbrakeProfileStorageTests
{
    [Fact]
    public async Task Save_Then_Load_Roundtrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hb-{Guid.NewGuid():N}.json");
        try
        {
            var storage = new JsonHandbrakeProfileStorage(path);
            var profile = new HandbrakeProfile(
                Sensor: 2, InputMin: 10, InputMax: 4000, Invert: false, Smooth: 20,
                Curve: new double[] { 0, 20, 40, 60, 80, 100 }, LoadCellScale: 1000,
                ButtonThreshold: 65, ButtonEnabled: true);
            await storage.SaveAsync(profile);
            var loaded = await storage.LoadAsync();
            Assert.NotNull(loaded);
            Assert.Equal(65, loaded!.ButtonThreshold);
            Assert.True(loaded.ButtonEnabled);
            Assert.Equal(2, loaded.Sensor);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Load_Returns_Null_When_Missing()
    {
        var storage = new JsonHandbrakeProfileStorage(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json"));
        Assert.Null(await storage.LoadAsync());
    }
}
