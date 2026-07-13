using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>
/// Página de ajustes para um conjunto curado de settings (ex.: "Base do Volante",
/// "Avançado"). Carrega os valores do dispositivo ao conectar.
/// </summary>
public sealed class SettingsGroupViewModel : ViewModelBase
{
    private readonly DeviceSession _session;

    public string Title { get; }
    public IReadOnlyList<SettingFieldViewModel> Fields { get; }

    public SettingsGroupViewModel(DeviceSession session, string title, IEnumerable<SettingId> ids)
    {
        _session = session;
        Title = title;
        Fields = ids.Select(id => new SettingFieldViewModel(session, SettingsSchema.Get(id))).ToList();
        _session.Connected += OnConnected;
    }

    public async Task LoadAsync()
    {
        foreach (var field in Fields)
            await field.LoadAsync();
    }

    private void OnConnected(object? sender, EventArgs e) => _ = LoadAsync();

    public override void Dispose()
    {
        _session.Connected -= OnConnected;
        foreach (var field in Fields)
            field.Dispose();
        base.Dispose();
    }
}
