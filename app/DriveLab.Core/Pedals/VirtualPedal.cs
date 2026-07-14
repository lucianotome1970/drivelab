using DriveLab.Core.Settings;

namespace DriveLab.Core.Pedals;

/// <summary>Modelo device-side de um pedal: aplica o pipeline P0 (normaliza→inverte→curva→suaviza).</summary>
public sealed class VirtualPedal
{
    public byte SensorType { get; set; }
    public ushort InputMin { get; set; }
    public ushort InputMax { get; set; } = 4095;
    public bool Invert { get; set; }
    public byte Smooth { get; set; }
    public ushort LoadCellScale { get; set; } = 1000;
    public double[] CurvePoints { get; } = { 0, 20, 40, 60, 80, 100 };

    private double _smoothed;

    public ushort RawInput { get; private set; }
    public ushort Output { get; private set; }

    public void SetRawInput(ushort raw)
    {
        RawInput = raw;
        var target = PedalCurve.ToOutput(raw, InputMin, InputMax, Invert, CurvePoints);
        var alpha = Math.Clamp(Smooth / 100.0, 0.0, 0.95);
        _smoothed = _smoothed * alpha + target * (1.0 - alpha);
        Output = (ushort)Math.Round(_smoothed);
    }
}
