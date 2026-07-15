// ============================================================================
//  DriveLab
//  VirtualBaseTests.cs — Testes do VirtualBase: física do volante (força, mola, limites).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Simulator;

namespace DriveLab.Tests.Simulator;

public class VirtualBaseTests
{
    private static VirtualBase StepMany(VirtualBase wheel, int steps, double dt = 0.01)
    {
        for (var i = 0; i < steps; i++) wheel.Step(dt);
        return wheel;
    }

    [Fact]
    public void Constant_Force_Moves_Wheel_In_Force_Direction()
    {
        var wheel = new VirtualBase { TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 0.5, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);

        StepMany(wheel, 50);

        Assert.True(wheel.AngleRad > 0, "wheel should rotate positive under positive constant force");
    }

    [Fact]
    public void Spring_Returns_Wheel_Toward_Center()
    {
        var wheel = new VirtualBase { TotalStrength01 = 1.0 };

        // Empurra pra fora com força constante.
        wheel.SetInputs(constant: 0.6, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);
        StepMany(wheel, 40);
        var displaced = wheel.AngleRad;

        // Remove a constante, liga mola + damper.
        wheel.SetInputs(constant: 0, spring: 1.0, periodic: 0, damper: 0.5, forceDrop01: 0);
        StepMany(wheel, 400);

        Assert.True(Math.Abs(wheel.AngleRad) < Math.Abs(displaced), "spring should pull back toward center");
    }

    [Fact]
    public void ResetCenter_Zeroes_Angle_And_Velocity()
    {
        var wheel = new VirtualBase { TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 0.5, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);
        StepMany(wheel, 30);

        wheel.ResetCenter();

        Assert.Equal(0, wheel.AngleRad);
        Assert.Equal(0, wheel.VelocityRad);
    }

    [Fact]
    public void Force_Disabled_Stops_Wheel_And_Holds_Angle()
    {
        var wheel = new VirtualBase { TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 0.8, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);
        StepMany(wheel, 50);
        Assert.True(Math.Abs(wheel.AngleRad) > 0, "wheel should have moved while force enabled");

        // Desabilita a força: o volante deve parar (sem torque, sem velocidade) e segurar o ângulo.
        wheel.ForceEnabled = false;
        var angleAtDisable = wheel.AngleRad;
        StepMany(wheel, 200);

        Assert.Equal(0, wheel.VelocityRad);
        Assert.Equal(angleAtDisable, wheel.AngleRad);
        Assert.Equal(0, wheel.TorqueNormalized);
    }

    [Fact]
    public void ResetCenter_Holds_At_Zero_While_Force_Disabled()
    {
        var wheel = new VirtualBase { TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 0.8, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);
        StepMany(wheel, 50);

        wheel.ForceEnabled = false;
        wheel.ResetCenter();
        StepMany(wheel, 200);

        Assert.Equal(0, wheel.AngleRad);
    }

    [Fact]
    public void Angle_Is_Clamped_To_Half_Range()
    {
        var wheel = new VirtualBase { MotionRangeDeg = 90, TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 1.0, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);

        StepMany(wheel, 2000);

        var halfRangeRad = (90.0 / 2.0) * Math.PI / 180.0;
        Assert.True(wheel.AngleRad <= halfRangeRad + 1e-6);
    }

    [Fact]
    public void PositionNormalized_Is_Plus_10000_At_Positive_Limit()
    {
        var wheel = new VirtualBase { MotionRangeDeg = 90, TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 1.0, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);

        StepMany(wheel, 2000);

        Assert.Equal(10000, wheel.PositionNormalized);
    }

    [Fact]
    public void PositionNormalized_Is_Minus_10000_At_Negative_Limit()
    {
        var wheel = new VirtualBase { MotionRangeDeg = 90, TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: -1.0, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);

        StepMany(wheel, 2000);

        var halfRangeRad = (90.0 / 2.0) * Math.PI / 180.0;
        Assert.True(wheel.AngleRad >= -halfRangeRad - 1e-6);
        Assert.Equal(-10000, wheel.PositionNormalized);
    }

    [Fact]
    public void Telemetry_Getters_Report_Sane_Values_Under_Positive_Force()
    {
        var wheel = new VirtualBase { TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 0.5, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);

        StepMany(wheel, 50);

        Assert.True(wheel.AngleDeciDeg > 0, "angle telemetry should be positive under positive constant force");
        Assert.InRange((int)wheel.TorqueNormalized, -10000, 10000);
    }
}
