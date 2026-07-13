using Avalonia.Controls;
using Avalonia.Interactivity;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Views;

public partial class MainWindow : Window
{
    private TestWindow? _testWindow;

    public MainWindow() => InitializeComponent();

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
