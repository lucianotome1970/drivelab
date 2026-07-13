using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Especificação de uma aba: cabeçalho + settings que ela contém.</summary>
public sealed record SettingsTabSpec(string Header, IReadOnlyList<SettingId> Ids);

/// <summary>
/// Página de ajustes com abas (ex.: "Base do Volante" → Basic / Advanced / Hardware).
/// Cada aba é um <see cref="SettingsGroupViewModel"/> que carrega sozinho ao conectar.
/// </summary>
public sealed class SettingsPageViewModel : ViewModelBase
{
    public string Title { get; }
    public IReadOnlyList<SettingsGroupViewModel> Tabs { get; }

    public SettingsPageViewModel(DeviceSession session, string title, IEnumerable<SettingsTabSpec> tabs)
    {
        Title = title;
        Tabs = tabs.Select(t => new SettingsGroupViewModel(session, t.Header, t.Ids)).ToList();
    }

    public override void Dispose()
    {
        foreach (var tab in Tabs)
            tab.Dispose();
        base.Dispose();
    }
}
