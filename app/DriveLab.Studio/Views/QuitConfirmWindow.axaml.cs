// ============================================================================
//  DriveLab
//  QuitConfirmWindow.axaml.cs — Code-behind de QuitConfirmWindow: confirma ou cancela o fechamento do app.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DriveLab.Studio.Views;

public partial class QuitConfirmWindow : Window
{
    public QuitConfirmWindow() => InitializeComponent();

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
