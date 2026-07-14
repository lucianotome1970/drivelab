using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace DriveLab.Studio.Converters;

/// <summary>
/// Posiciona a bolinha do "máximo atingido" na barra de calibração:
/// entrada [valor 0..4095, largura da barra em px] → Thickness com Left = fração × largura (centrado).
/// </summary>
public sealed class CalMarkerMarginConverter : IMultiValueConverter
{
    private const double Max = 4095.0;
    private const double DotRadius = 6.0;

    public static readonly CalMarkerMarginConverter Instance = new();

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2
            && values[0] is not null && values[1] is double width && width > 0
            && double.TryParse(System.Convert.ToString(values[0], CultureInfo.InvariantCulture),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            var left = Math.Clamp(value / Max, 0, 1) * width - DotRadius;
            return new Thickness(Math.Max(0, left), 0, 0, 0);
        }
        return new Thickness(0);
    }
}
