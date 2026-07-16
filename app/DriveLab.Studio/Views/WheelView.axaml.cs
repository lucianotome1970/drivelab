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

    // Clicar num marcador seleciona o botão (p/ escolher a cor). O "pressionar" que acende os
    // controles vem da telemetria: no hardware, do firmware; no /simulator, do demo auto-aleatório
    // (SimulatorWheelTransport). Por isso o mouse não seta IsPressed aqui (evita conflito 30 Hz).

    private void OnMarkerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { DataContext: WheelButtonViewModel c } && DataContext is WheelViewModel vm)
            vm.SelectButtonCommand.Execute(c);
    }

    // Mantidos por compatibilidade com os bindings da view; sem ação (a telemetria dirige os controles).
    private void OnPaddlePressed(object? sender, PointerPressedEventArgs e) { }
    private void OnControlReleased(object? sender, PointerReleasedEventArgs e) { }
}
