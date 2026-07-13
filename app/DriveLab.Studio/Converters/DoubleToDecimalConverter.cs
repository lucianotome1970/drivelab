using System.Globalization;
using Avalonia.Data.Converters;

namespace DriveLab.Studio.Converters;

/// <summary>
/// Converte entre <c>double</c> (usado nos view models) e <c>decimal?</c>
/// (tipo exigido por <see cref="Avalonia.Controls.NumericUpDown.Value"/>).
/// </summary>
public sealed class DoubleToDecimalConverter : IValueConverter
{
    public static readonly DoubleToDecimalConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? (decimal)d : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is decimal m ? (double)m : (value is null ? 0d : System.Convert.ToDouble(value, culture));
}
