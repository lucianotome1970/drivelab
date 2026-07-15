// ============================================================================
//  DriveLab
//  MainWindow.axaml.cs — Code-behind de MainWindow: fecha o app com confirmação e abre a janela de teste de força.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Views;

public partial class MainWindow : Window
{
    private TestWindow? _testWindow;

    public MainWindow() => InitializeComponent();

    private async void CloseApp_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = await new QuitConfirmWindow().ShowDialog<bool>(this);
        if (!confirmed)
            return;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Close();
    }

    private void OpenTest_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        // Instância única: se já aberta, só traz pra frente.
        if (_testWindow is not null)
        {
            _testWindow.Activate();
            return;
        }

        _testWindow = new TestWindow { DataContext = vm.Test };
        _testWindow.Closed += (_, _) => _testWindow = null;
        _testWindow.Show(this); // modeless: a janela principal (volante) continua atualizando ao fundo
    }
}
