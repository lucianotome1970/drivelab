using CommunityToolkit.Mvvm.ComponentModel;

namespace DriveLab.Studio.ViewModels;

/// <summary>Um botão do volante no mock: nome, posição normalizada (0-1) sobre a imagem e
/// cor de LED. Left/Top já em pixels para um canvas de 480 (menos o raio do marcador).</summary>
public partial class WheelButtonViewModel : ObservableObject
{
    public const double CanvasSize = 480;
    public const double MarkerRadius = 11;

    public string Name { get; }
    public double X { get; }
    public double Y { get; }
    public double Left => X * CanvasSize - MarkerRadius;
    public double Top => Y * CanvasSize - MarkerRadius;

    [ObservableProperty] private string _colorHex;
    [ObservableProperty] private bool _isSelected;

    public WheelButtonViewModel(string name, double x, double y, string colorHex)
    {
        Name = name;
        X = x;
        Y = y;
        _colorHex = colorHex;
    }
}
