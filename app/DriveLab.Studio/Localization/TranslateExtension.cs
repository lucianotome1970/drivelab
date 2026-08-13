// ============================================================================
//  DriveLab
//  TranslateExtension.cs — Markup extension de tradução para XAML, resolvendo a chave via LocalizationManager.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

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
