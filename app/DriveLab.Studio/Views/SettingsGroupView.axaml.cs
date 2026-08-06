// ============================================================================
//  DriveLab
//  SettingsGroupView.axaml.cs — Code-behind de SettingsGroupView. Expõe Header e Footer: conteúdo opcional
//  renderizado DENTRO do scroll, acima e abaixo dos campos, para que role JUNTO com eles.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia;
using Avalonia.Controls;

namespace DriveLab.Studio.Views;

public partial class SettingsGroupView : UserControl
{
    /// <summary>Conteúdo opcional exibido dentro do ScrollViewer, ACIMA das colunas de campos (rola junto).
    /// Usado pela aba Hardware para o monitor de telemetria rolar com os parâmetros, em vez de ficar fixo
    /// no topo comendo altura útil.</summary>
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<SettingsGroupView, object?>(nameof(Header));

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

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
