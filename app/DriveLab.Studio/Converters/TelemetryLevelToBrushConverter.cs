using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Converters;

/// <summary>Converte um <see cref="TelemetryLevel"/> em um <see cref="IBrush"/> (verde/âmbar/vermelho).</summary>
public sealed class TelemetryLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TelemetryLevel.Critical => new SolidColorBrush(Color.Parse("#E5484D")),
        TelemetryLevel.Warning => new SolidColorBrush(Color.Parse("#F5A623")),
        _ => new SolidColorBrush(Color.Parse("#3DD68C")),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
