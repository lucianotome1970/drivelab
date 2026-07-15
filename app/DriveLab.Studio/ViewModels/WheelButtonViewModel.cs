// ============================================================================
//  DriveLab
//  WheelButtonViewModel.cs — VM de um controle do volante no mock: posição, diâmetro e cor de LED.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;

namespace DriveLab.Studio.ViewModels;

/// <summary>Um controle do volante no mock: nome, posição normalizada (0-1) sobre a imagem,
/// diâmetro do marcador e cor de LED. Left/Top já em pixels para um canvas de 480 (centrado
/// no controle). <see cref="IsPressed"/> = aceso: acionado pela simulação (segurar o mouse) OU,
/// no futuro, pela telemetria de botões do firmware — mesmo caminho, mesmo efeito visual.</summary>
public partial class WheelButtonViewModel : ObservableObject
{
    public const double CanvasSize = 480;

    public string Name { get; }
    public double X { get; }
    public double Y { get; }
    public double Diameter { get; }
    public double Left => X * CanvasSize - Diameter / 2;
    public double Top => Y * CanvasSize - Diameter / 2;

    [ObservableProperty] private string _colorHex;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isPressed;

    public WheelButtonViewModel(string name, double x, double y, string colorHex, double diameter = 22)
    {
        Name = name;
        X = x;
        Y = y;
        _colorHex = colorHex;
        Diameter = diameter;
    }
}
