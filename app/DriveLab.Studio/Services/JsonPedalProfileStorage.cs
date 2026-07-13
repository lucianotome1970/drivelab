using System.IO;
using System.Text.Json;

namespace DriveLab.Studio.Services;

/// <summary>Perfil local em JSON num caminho fixo do usuário (sem diálogo, escopo desta entrega).</summary>
public sealed class JsonPedalProfileStorage : IPedalProfileStorage
{
    private readonly string _path;

    public JsonPedalProfileStorage(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriveLab", "pedals.json");
    }

    public async Task SaveAsync(PedalProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, profile);
    }

    public async Task<PedalProfile?> LoadAsync()
    {
        if (!File.Exists(_path))
            return null;
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<PedalProfile>(stream);
    }
}
