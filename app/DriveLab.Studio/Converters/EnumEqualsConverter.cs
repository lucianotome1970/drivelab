using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DriveLab.Studio.Converters;

/// <summary>true quando value.ToString() == ConverterParameter (para chips de enum selecionados).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is string s && value.ToString() == s;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
