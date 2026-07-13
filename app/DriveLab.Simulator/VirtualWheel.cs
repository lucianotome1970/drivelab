namespace DriveLab.Simulator;

public sealed class VirtualWheel
{
    private const double Inertia = 0.05;      // kg·m² virtual
    private const double SpringStiffness = 4;  // ganho de centralização
    private const double DamperCoeff = 2;      // ganho de damping

    private double _constant;
    private double _spring;
    private double _periodic;
    private double _damper;
    private double _forceDrop01;
    private double _lastTorque;

    public double MotionRangeDeg { get; set; } = 900;
    public double SpringGain { get; set; } = 1.0;
    public double DamperGain { get; set; } = 1.0;
    public double TotalStrength01 { get; set; } = 1.0;

    /// <summary>
    /// Motor habilitado. Quando desligado, o volante não recebe torque nenhum
    /// (fica parado no ângulo atual) — espelha o "força habilitada" do dispositivo.
    /// </summary>
    public bool ForceEnabled { get; set; } = true;

    public double AngleRad { get; private set; }
    public double VelocityRad { get; private set; }

    public void SetInputs(double constant, double spring, double periodic, double damper, double forceDrop01)
    {
        _constant = constant;
        _spring = spring;
        _periodic = periodic;
        _damper = damper;
        _forceDrop01 = Math.Clamp(forceDrop01, 0, 1);
    }

    public void ResetCenter()
    {
        AngleRad = 0;
        VelocityRad = 0;
    }

    public void Step(double dt)
    {
        if (!ForceEnabled)
        {
            // Motor desligado: sem torque, sem velocidade. Segura o ângulo atual.
            VelocityRad = 0;
            _lastTorque = 0;
            return;
        }

        var halfRangeRad = HalfRangeRad();
        var position = halfRangeRad > 0 ? AngleRad / halfRangeRad : 0; // −1..+1

        var springTorque = -position * _spring * SpringGain * SpringStiffness;
        var damperTorque = -VelocityRad * _damper * DamperGain * DamperCoeff;
        var netTorque = (_constant + _periodic) + springTorque + damperTorque;

        netTorque *= TotalStrength01 * (1.0 - _forceDrop01);
        _lastTorque = netTorque;

        var accel = netTorque / Inertia;
        VelocityRad += accel * dt;
        AngleRad += VelocityRad * dt;

        if (AngleRad > halfRangeRad)
        {
            AngleRad = halfRangeRad;
            if (VelocityRad > 0) VelocityRad = 0;
        }
        else if (AngleRad < -halfRangeRad)
        {
            AngleRad = -halfRangeRad;
            if (VelocityRad < 0) VelocityRad = 0;
        }
    }

    public short PositionNormalized => ToInt16Normalized(NormalizedPosition());

    public short AngleDeciDeg => (short)Math.Clamp(AngleRad * 180.0 / Math.PI * 10.0, short.MinValue, short.MaxValue);

    public short TorqueNormalized => ToInt16Normalized(Math.Clamp(_lastTorque, -1, 1));

    private double HalfRangeRad() => MotionRangeDeg / 2.0 * Math.PI / 180.0;

    private double NormalizedPosition()
    {
        var halfRangeRad = HalfRangeRad();
        return halfRangeRad > 0 ? Math.Clamp(AngleRad / halfRangeRad, -1, 1) : 0;
    }

    private static short ToInt16Normalized(double value) =>
        (short)Math.Round(Math.Clamp(value, -1, 1) * 10000);
}
