// ============================================================================
//  DriveLab
//  WheelView.axaml.cs — Code-behind de WheelView: simula pressionamento de botões/pás no modo simulador.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

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
            vm.SelectButtonCommand.Execute(c);      // seleção p/ a paleta: sempre (config de cor)
            if (!vm.IsSimulator || !vm.IsConnected)  // simular pressionar só no /simulator E após Conectar
                return;
            vm.SetControlPressed(c.Name, true);
            e.Pointer.Capture(ctl);                 // garante o PointerReleased mesmo saindo do controle
        }
    }

    private void OnPaddlePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: WheelButtonViewModel c } ctl && DataContext is WheelViewModel vm)
        {
            if (!vm.IsSimulator || !vm.IsConnected)  // pás simulam pressão só no /simulator E após Conectar
                return;
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
