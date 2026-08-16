// ============================================================================
//  DriveLab
//  PreferencesWindow.axaml.cs — Code-behind da janela de preferências do app.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DriveLab.Studio.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow() => InitializeComponent();

    private void Fechar_Click(object? sender, RoutedEventArgs e) => Close();
}
