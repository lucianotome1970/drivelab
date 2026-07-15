using Avalonia.Controls;
using Avalonia.Input;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Views;

public partial class WheelView : UserControl
{
    public WheelView() => InitializeComponent();

    // Simulação de pressionamento (momentâneo): segurar acende, soltar apaga.
    // Rota pelo MESMO WheelViewModel.SetControlPressed que a telemetria do firmware usará.

    private void OnMarkerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: WheelButtonViewModel c } ctl && DataContext is WheelViewModel vm)
        {
            vm.SelectButtonCommand.Execute(c);      // clique também seleciona p/ a paleta
            vm.SetControlPressed(c.Name, true);
            e.Pointer.Capture(ctl);                 // garante o PointerReleased mesmo saindo do controle
        }
    }

    private void OnPaddlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: WheelButtonViewModel c } ctl && DataContext is WheelViewModel vm)
        {
            vm.SetControlPressed(c.Name, true);
            e.Pointer.Capture(ctl);
        }
    }

    private void OnControlReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control { DataContext: WheelButtonViewModel c } && DataContext is WheelViewModel vm)
            vm.SetControlPressed(c.Name, false);
        e.Pointer.Capture(null);
    }
}
