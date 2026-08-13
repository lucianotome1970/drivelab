// ============================================================================
//  DriveLab
//  HomeView.axaml.cs — Code-behind de HomeView: clique no card navega para o módulo.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Views;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    // Clique no card (não em um botão/slider dentro dele) abre a tela do módulo,
    // desde que o dispositivo esteja detectado (o VM verifica a conexão).
    private void Card_Tapped(object? sender, TappedEventArgs e)
    {
        if (IsFromInteractive(e.Source as Visual))
            return;
        if (sender is Control card && card.Tag is string key && DataContext is HomeViewModel vm)
            vm.OpenModule(key);
    }

    // True se o clique veio de um controle interativo (botão, slider, toggle, caixa de texto)
    // dentro do card — nesses casos NÃO navegamos.
    private static bool IsFromInteractive(Visual? src)
    {
        for (var v = src; v is not null; v = v.GetVisualParent())
        {
            if (v is Button or ToggleSwitch or Slider or TextBox)
                return true;
            if (v is HomeView)
                break;
        }
        return false;
    }
}
