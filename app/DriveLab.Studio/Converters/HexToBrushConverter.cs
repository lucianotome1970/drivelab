using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DriveLab.Studio.Converters;

/// <summary>Hex "#RRGGBB" → SolidColorBrush (para o glow do LED e os chips da paleta).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && Color.TryParse(hex, out var color))
            return new SolidColorBrush(color);
        return Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
