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

    /// <summary>Fechar a janela ESCONDE o app em vez de encerrá-lo, quando a preferência está ligada.
    ///
    /// <para>Os atalhos de centralizar — tecla, botão do aro — vivem no Studio: é ele que escuta o
    /// teclado, lê os botões e manda o comando para a base. Encerrado, ninguém escuta, e o atalho
    /// deixa de existir. Esconder mantém isso funcionando com a janela fora do caminho.</para>
    ///
    /// <para>⚠️ MINIMIZADO CUSTA QUASE NADA, e é de propósito: com a janela escondida o Avalonia para
    /// de desenhar — que é o grosso do trabalho — e sobra o que precisa continuar: o hook de teclado,
    /// a varredura dos botões e a conexão com a base. Nada de gráfico, nada de layout, nada de
    /// animação.</para>
    ///
    /// <para>Quem desliga a preferência volta a ter o X encerrando de verdade — e aí os atalhos param
    /// junto, que é a consequência honesta da escolha.</para></summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (App.Preferencias.ManterNaBandeja && !_saindoDeVerdade)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    /// <summary>Marca que o encerramento foi PEDIDO (botão sair / menu da bandeja), para o OnClosing
    /// acima não transformar a saída em "esconder" e deixar o app impossível de fechar.</summary>
    private bool _saindoDeVerdade;

    private async void CloseApp_Click(object? sender, RoutedEventArgs e)
    {
        var confirmed = await new QuitConfirmWindow().ShowDialog<bool>(this);
        if (!confirmed)
            return;

        _saindoDeVerdade = true;
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
