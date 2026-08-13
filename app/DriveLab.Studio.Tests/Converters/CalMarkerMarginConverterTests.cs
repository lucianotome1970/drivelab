// ============================================================================
//  DriveLab
//  CalMarkerMarginConverterTests.cs — Testes de CalMarkerMarginConverter (posição do marcador de calibração).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Globalization;
using Avalonia;
using DriveLab.Studio.Converters;
using Xunit;

namespace DriveLab.Studio.Tests.Converters;

public class CalMarkerMarginConverterTests
{
    private static double Left(object? value, object? width) =>
        ((Thickness)CalMarkerMarginConverter.Instance.Convert(
            new[] { value, width }, typeof(Thickness), null, CultureInfo.InvariantCulture)).Left;

    [Fact]
    public void Positions_At_Fraction_Of_Width_Centered()
    {
        // 2048/4095 * 200 - 6 ≈ 94
        Assert.InRange(Left(2048, 200.0), 92, 96);
    }

    [Fact]
    public void Zero_Width_Or_Missing_Is_Left_Zero()
    {
        Assert.Equal(0, Left(2048, 0.0));
        Assert.Equal(0, Left(null, 200.0));
    }
}
