using System.Globalization;
using Avalonia.Media;
using DriveLab.Studio.Converters;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.Converters;

public class TelemetryLevelToBrushConverterTests
{
    private static Color Convert(TelemetryLevel level) =>
        ((SolidColorBrush)new TelemetryLevelToBrushConverter().Convert(
            level, typeof(IBrush), null, CultureInfo.InvariantCulture)).Color;

    [Fact]
    public void Ok_Maps_To_Green() => Assert.Equal(Color.Parse("#3DD68C"), Convert(TelemetryLevel.Ok));

    [Fact]
    public void Warning_Maps_To_Amber() => Assert.Equal(Color.Parse("#F5A623"), Convert(TelemetryLevel.Warning));

    [Fact]
    public void Critical_Maps_To_Red() => Assert.Equal(Color.Parse("#E5484D"), Convert(TelemetryLevel.Critical));
}
