using DriveLab.Core.Settings;

namespace DriveLab.Studio.ViewModels;

/// <summary>Especificação de uma aba de settings: cabeçalho + settings que ela contém.</summary>
public sealed record SettingsTabSpec(string Header, IReadOnlyList<SettingId> Ids);

/// <summary>Uma aba da página: cabeçalho + conteúdo (renderizado pelo ViewLocator).</summary>
public sealed record PageTab(string Header, ViewModelBase Content);

/// <summary>
/// Página com abas (ex.: "Base do Volante" → Basic / Advanced / Hardware / Telemetria).
/// Cada aba tem seu próprio ViewModel/View; a página só as agrega e descarta.
/// </summary>
public sealed class SettingsPageViewModel : ViewModelBase
{
    public string Title { get; }
    public IReadOnlyList<PageTab> Tabs { get; }

    public SettingsPageViewModel(string title, IEnumerable<PageTab> tabs)
    {
        Title = title;
        Tabs = tabs.ToList();
    }

    public override void Dispose()
    {
        foreach (var tab in Tabs)
            tab.Content.Dispose();
        base.Dispose();
    }
}
