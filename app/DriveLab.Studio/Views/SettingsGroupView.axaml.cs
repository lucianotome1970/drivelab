// ============================================================================
//  DriveLab
//  SettingsGroupView.axaml.cs — Code-behind de SettingsGroupView. Expõe uma propriedade Footer (conteúdo
//  opcional renderizado DENTRO do scroll, abaixo dos campos) — usada pela aba Hardware p/ o export rolar junto.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia;
using Avalonia.Controls;

namespace DriveLab.Studio.Views;

public partial class SettingsGroupView : UserControl
{
    /// <summary>Conteúdo opcional exibido dentro do ScrollViewer, ABAIXO das colunas de campos (rola junto).</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<SettingsGroupView, object?>(nameof(Footer));

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public SettingsGroupView() => InitializeComponent();
}
