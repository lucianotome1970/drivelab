using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DriveLab.Studio.Services;

/// <summary>Perfil do volante em JSON local (espelha JsonPedalProfileStorage). Enums gravados
/// como texto para o arquivo ser legível/estável.</summary>
public sealed class JsonWheelProfileStorage : IWheelProfileStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public JsonWheelProfileStorage(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DriveLab", "wheel.json");
    }

    public async Task SaveAsync(WheelProfile profile)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, profile, Options);
    }

    public async Task<WheelProfile?> LoadAsync()
    {
        if (!File.Exists(_path))
            return null;
        await using var stream = File.OpenRead(_path);
        return await JsonSerializer.DeserializeAsync<WheelProfile>(stream, Options);
    }
}
