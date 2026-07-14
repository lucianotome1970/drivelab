namespace DriveLab.Core.Settings;

/// <summary>
/// Semântica oficial da curva/pipeline de um pedal (contrato P0). O firmware espelha
/// esta matemática. Pontos = 6 saídas em % (0..100) nos inputs 0/20/40/60/80/100%.
/// </summary>
public static class PedalCurve
{
    public static double Evaluate(IReadOnlyList<double> pointsPercent, double input01)
    {
        var x = Math.Clamp(input01, 0.0, 1.0);
        var scaled = x * (pointsPercent.Count - 1); // 0..(N-1)
        var i = (int)Math.Floor(scaled);
        if (i >= pointsPercent.Count - 1)
            return pointsPercent[^1] / 100.0;
        var frac = scaled - i;
        var y = pointsPercent[i] + (pointsPercent[i + 1] - pointsPercent[i]) * frac;
        return y / 100.0;
    }

    public static ushort ToOutput(ushort raw, ushort inputMin, ushort inputMax, bool invert,
        IReadOnlyList<double> pointsPercent, double deadLow = 0, double deadHigh = 100)
    {
        double norm = inputMax > inputMin
            ? Math.Clamp((double)(raw - inputMin) / (inputMax - inputMin), 0.0, 1.0)
            : 0.0;
        if (invert)
            norm = 1.0 - norm;

        var lo = deadLow / 100.0;
        var hi = deadHigh / 100.0;
        norm = hi > lo ? Math.Clamp((norm - lo) / (hi - lo), 0.0, 1.0) : 0.0;

        var outp = Evaluate(pointsPercent, norm);
        return (ushort)Math.Round(Math.Clamp(outp, 0.0, 1.0) * 65535.0);
    }
}
