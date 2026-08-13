// ============================================================================
//  DriveLab
//  SettingValueTests.cs — Testes de round-trip do SettingValue.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;

namespace DriveLab.Tests.Settings;

public class SettingValueTests
{
    [Theory]
    [InlineData(SettingType.UInt8, 200, 1)]
    [InlineData(SettingType.Int8, -1, 1)]
    [InlineData(SettingType.UInt16, 900, 2)]
    [InlineData(SettingType.Int16, -10000, 2)]
    public void Integer_Values_RoundTrip(SettingType type, double value, int expectedBytes)
    {
        var setting = new SettingValue(type, value);
        Span<byte> buffer = stackalloc byte[8];

        var written = setting.WriteValue(buffer);
        var parsed = SettingValue.ReadValue(type, buffer);

        Assert.Equal(expectedBytes, written);
        Assert.Equal(value, parsed.AsDouble);
        Assert.Equal(type, parsed.Type);
    }

    [Theory]
    [InlineData(31.6, 32)]   // slider fracionário → arredonda (não trunca)
    [InlineData(31.4, 31)]
    [InlineData(31.5, 32)]   // meio → away-from-zero
    [InlineData(62.7, 63)]
    public void Integer_Rounds_Not_Truncates(double value, byte expected)
    {
        var setting = new SettingValue(SettingType.UInt8, value);
        Span<byte> buffer = stackalloc byte[1];
        setting.WriteValue(buffer);
        Assert.Equal(expected, buffer[0]);
    }

    [Fact]
    public void Float_Value_RoundTrips()
    {
        var setting = new SettingValue(SettingType.Float, 0.05);
        Span<byte> buffer = stackalloc byte[8];

        var written = setting.WriteValue(buffer);
        var parsed = SettingValue.ReadValue(SettingType.Float, buffer);

        Assert.Equal(4, written);
        Assert.Equal(0.05, parsed.AsDouble, precision: 5);
    }
}
