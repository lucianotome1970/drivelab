using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DriveLab.Studio.Converters;

/// <summary>
/// Converte um <c>bool</c> em um <see cref="IBrush"/>, escolhendo entre um par de pincéis
/// (true/false) informado via <c>ConverterParameter="corTrue|corFalse"</c>, ou usando o
/// par padrão (destaque laranja de acento / cinza neutro) quando nenhum parâmetro é dado.
/// Usado para indicar estado do botão digital do freio de mão (pressionado/solto).
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();

    private static readonly IBrush DefaultTrueBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x00));
    private static readonly IBrush DefaultFalseBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0x3F, 0x47));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;

        if (parameter is string s)
        {
            var parts = s.Split('|');
            if (parts.Length == 2)
            {
                var chosen = flag ? parts[0] : parts[1];
                if (Color.TryParse(chosen, out var color))
                    return new SolidColorBrush(color);
            }
        }

        return flag ? DefaultTrueBrush : DefaultFalseBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
