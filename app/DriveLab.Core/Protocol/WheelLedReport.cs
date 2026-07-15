// ============================================================================
//  DriveLab
//  WheelLedReport.cs — Report de saída de cores RGB dos LEDs do rim (brilho + array de LEDs) serializado para bytes.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Protocol;

public readonly record struct WheelLedColor(byte R, byte G, byte B);

/// <summary>Wire report de LEDs (out, 0x18). Layout: [0]=count, [1]=brightness, [2+3i..]=R,G,B por LED.</summary>
public sealed class WheelLedReport
{
    public const int MaxLeds = 20;

    public byte Brightness { get; }
    public IReadOnlyList<WheelLedColor> Leds { get; }

    public WheelLedReport(byte brightness, IReadOnlyList<WheelLedColor> leds)
    {
        if (leds.Count > MaxLeds)
            throw new ArgumentException($"No máximo {MaxLeds} LEDs.", nameof(leds));
        Brightness = brightness;
        Leds = leds;
    }

    public byte[] ToBytes()
    {
        var buffer = new byte[ReportConstants.ReportSize];
        buffer[0] = (byte)Leds.Count;
        buffer[1] = Brightness;
        for (var i = 0; i < Leds.Count; i++)
        {
            var o = 2 + i * 3;
            buffer[o] = Leds[i].R;
            buffer[o + 1] = Leds[i].G;
            buffer[o + 2] = Leds[i].B;
        }
        return buffer;
    }

    public static WheelLedReport Parse(ReadOnlySpan<byte> src)
    {
        int count = src[0];
        if (count > MaxLeds) count = MaxLeds;
        var brightness = src[1];
        var leds = new WheelLedColor[count];
        for (var i = 0; i < count; i++)
        {
            var o = 2 + i * 3;
            leds[i] = new WheelLedColor(src[o], src[o + 1], src[o + 2]);
        }
        return new WheelLedReport(brightness, leds);
    }
}
