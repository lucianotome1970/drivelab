using DriveLab.Simulator;

namespace DriveLab.Tests.Simulator;

public class VirtualPedalTests
{
    [Fact]
    public void Output_Tracks_Raw_With_Linear_Curve_No_Smoothing()
    {
        var p = new VirtualPedal { InputMin = 0, InputMax = 4095, Smooth = 0 };
        p.SetRawInput(4095);
        Assert.Equal(65535, p.Output);
        p.SetRawInput(0);
        Assert.Equal(0, p.Output);
    }

    [Fact]
    public void Invert_Flips_Output()
    {
        var p = new VirtualPedal { InputMin = 0, InputMax = 4095, Invert = true, Smooth = 0 };
        p.SetRawInput(0);
        Assert.Equal(65535, p.Output);
    }

    [Fact]
    public void Smoothing_Lags_Toward_Target()
    {
        var p = new VirtualPedal { InputMin = 0, InputMax = 4095, Smooth = 80 };
        p.SetRawInput(4095);
        // com smoothing forte, uma amostra não chega ao alvo
        Assert.True(p.Output < 65535);
        Assert.True(p.Output > 0);
        for (var i = 0; i < 200; i++) p.SetRawInput(4095);
        Assert.InRange(p.Output, 65000, 65535); // converge
    }

    [Fact]
    public void CurvePoints_Affect_Output()
    {
        var p = new VirtualPedal { InputMin = 0, InputMax = 4095, Smooth = 0 };
        p.CurvePoints[0] = 0; p.CurvePoints[1] = 0; p.CurvePoints[2] = 0;
        p.CurvePoints[3] = 0; p.CurvePoints[4] = 0; p.CurvePoints[5] = 0; // curva zerada
        p.SetRawInput(4095);
        Assert.Equal(0, p.Output);
    }
}
