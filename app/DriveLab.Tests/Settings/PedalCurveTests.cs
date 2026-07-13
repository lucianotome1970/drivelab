using DriveLab.Core.Settings;

namespace DriveLab.Tests.Settings;

public class PedalCurveTests
{
    private static readonly double[] Linear = { 0, 20, 40, 60, 80, 100 };

    [Fact]
    public void Evaluate_Endpoints_Match_First_And_Last_Point()
    {
        Assert.Equal(0.0, PedalCurve.Evaluate(Linear, 0.0), precision: 6);
        Assert.Equal(1.0, PedalCurve.Evaluate(Linear, 1.0), precision: 6);
    }

    [Fact]
    public void Evaluate_Linear_Curve_Is_Identity()
    {
        Assert.Equal(0.5, PedalCurve.Evaluate(Linear, 0.5), precision: 6);
        Assert.Equal(0.3, PedalCurve.Evaluate(Linear, 0.3), precision: 6);
    }

    [Fact]
    public void Evaluate_Interpolates_Between_Points()
    {
        // Curva com salto: input 40%..60% vai de 10 a 90 (%)
        var pts = new double[] { 0, 0, 10, 90, 100, 100 };
        // input 0.5 fica no meio do segmento 40%..60% -> (10+90)/2 = 50%
        Assert.Equal(0.50, PedalCurve.Evaluate(pts, 0.5), precision: 6);
    }

    [Fact]
    public void Evaluate_Clamps_Input()
    {
        Assert.Equal(0.0, PedalCurve.Evaluate(Linear, -1.0), precision: 6);
        Assert.Equal(1.0, PedalCurve.Evaluate(Linear, 2.0), precision: 6);
    }

    [Fact]
    public void ToOutput_Normalizes_Min_Max()
    {
        // raw no meio do range -> ~50% -> ~32768
        var outp = PedalCurve.ToOutput(2048, 0, 4095, invert: false, Linear);
        Assert.InRange(outp, 32000, 33500);
    }

    [Fact]
    public void ToOutput_Full_And_Empty()
    {
        Assert.Equal(0, PedalCurve.ToOutput(0, 0, 4095, false, Linear));
        Assert.Equal(65535, PedalCurve.ToOutput(4095, 0, 4095, false, Linear));
    }

    [Fact]
    public void ToOutput_Invert_Flips()
    {
        Assert.Equal(65535, PedalCurve.ToOutput(0, 0, 4095, invert: true, Linear));
        Assert.Equal(0, PedalCurve.ToOutput(4095, 0, 4095, invert: true, Linear));
    }

    [Fact]
    public void ToOutput_Degenerate_Range_Is_Zero()
    {
        Assert.Equal(0, PedalCurve.ToOutput(500, 1000, 1000, false, Linear));
    }
}
