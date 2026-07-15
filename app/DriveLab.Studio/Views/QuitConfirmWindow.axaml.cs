using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DriveLab.Studio.Views;

public partial class QuitConfirmWindow : Window
{
    public QuitConfirmWindow() => InitializeComponent();

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
