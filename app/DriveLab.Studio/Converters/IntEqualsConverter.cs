using System.Globalization;
using Avalonia.Data.Converters;

namespace DriveLab.Studio.Converters;

/// <summary>
/// Retorna <c>true</c> quando o valor inteiro ligado é igual ao parâmetro
/// (string ou número). Usado para destacar o preset de ângulo selecionado.
/// </summary>
public sealed class IntEqualsConverter : IValueConverter
{
    public static readonly IntEqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        var v = System.Convert.ToInt64(value, CultureInfo.InvariantCulture);
        return long.TryParse(parameter.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
            && v == p;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
