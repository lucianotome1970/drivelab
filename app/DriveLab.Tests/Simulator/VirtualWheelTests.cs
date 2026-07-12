using DriveLab.Simulator;

namespace DriveLab.Tests.Simulator;

public class VirtualWheelTests
{
    private static VirtualWheel StepMany(VirtualWheel wheel, int steps, double dt = 0.01)
    {
        for (var i = 0; i < steps; i++) wheel.Step(dt);
        return wheel;
    }

    [Fact]
    public void Constant_Force_Moves_Wheel_In_Force_Direction()
    {
        var wheel = new VirtualWheel { TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 0.5, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);

        StepMany(wheel, 50);

        Assert.True(wheel.AngleRad > 0, "wheel should rotate positive under positive constant force");
    }

    [Fact]
    public void Spring_Returns_Wheel_Toward_Center()
    {
        var wheel = new VirtualWheel { TotalStrength01 = 1.0 };

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
        var wheel = new VirtualWheel { TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 0.5, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);
        StepMany(wheel, 30);

        wheel.ResetCenter();

        Assert.Equal(0, wheel.AngleRad);
        Assert.Equal(0, wheel.VelocityRad);
    }

    [Fact]
    public void Angle_Is_Clamped_To_Half_Range()
    {
        var wheel = new VirtualWheel { MotionRangeDeg = 90, TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 1.0, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);

        StepMany(wheel, 2000);

        var halfRangeRad = (90.0 / 2.0) * Math.PI / 180.0;
        Assert.True(wheel.AngleRad <= halfRangeRad + 1e-6);
    }

    [Fact]
    public void PositionNormalized_Is_Plus_10000_At_Positive_Limit()
    {
        var wheel = new VirtualWheel { MotionRangeDeg = 90, TotalStrength01 = 1.0 };
        wheel.SetInputs(constant: 1.0, spring: 0, periodic: 0, damper: 0, forceDrop01: 0);

        StepMany(wheel, 2000);

        Assert.Equal(10000, wheel.PositionNormalized);
    }
}
