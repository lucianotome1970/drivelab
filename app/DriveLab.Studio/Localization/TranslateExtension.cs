using Avalonia.Markup.Xaml;

namespace DriveLab.Studio.Localization;

/// <summary>
/// Markup de tradução para XAML: <c>Text="{loc:Translate Connect}"</c>.
/// Resolve no carregamento da View (o idioma já foi definido no startup).
/// </summary>
public sealed class TranslateExtension : MarkupExtension
{
    public TranslateExtension() { }
    public TranslateExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => LocalizationManager.Get(Key);
}
