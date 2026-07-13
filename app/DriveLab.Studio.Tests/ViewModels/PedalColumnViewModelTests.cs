using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class PedalColumnViewModelTests
{
    private static (PedalColumnViewModel vm, FakePedalTransport t, PedalDeviceSession s) Make(bool connected)
    {
        var t = new FakePedalTransport();
        var s = new PedalDeviceSession(t, new ImmediateUiDispatcher());
        if (connected) s.ConnectAsync().GetAwaiter().GetResult();
        var vm = new PedalColumnViewModel(s, PedalIndex.Brake, "Freio");
        return (vm, t, s);
    }

    [Fact]
    public void Has_Six_Curve_Points()
    {
        var (vm, _, s) = Make(false);
        Assert.Equal(6, vm.Points.Count);
        Assert.Equal("0%", vm.Points[0].Label);
        Assert.Equal("100%", vm.Points[5].Label);
        s.Dispose();
    }

    [Fact]
    public void Editing_Point_Writes_When_Connected()
    {
        var (vm, t, s) = Make(connected: true);
        vm.Points[2].Value = 55;
        Assert.NotNull(t.LastWrite);
        Assert.Equal(PedalSettingId.CurvePoint2, t.LastWrite!.Value.id);
        Assert.Equal(PedalIndex.Brake, t.LastWrite.Value.pedal);
        Assert.Equal(55, t.LastWrite.Value.value.AsDouble);
        s.Dispose();
    }

    [Fact]
    public void Editing_Point_Does_Not_Write_When_Disconnected()
    {
        var (vm, t, s) = Make(connected: false);
        vm.Points[2].Value = 55;
        Assert.Null(t.LastWrite);
        s.Dispose();
    }

    [Fact]
    public void CanEdit_Follows_Connection()
    {
        var (vm, _, s) = Make(connected: false);
        Assert.False(vm.CanEdit);
        s.ConnectAsync().GetAwaiter().GetResult();
        Assert.True(vm.CanEdit);
        s.Dispose();
    }

    [Fact]
    public void ApplyPreset_Sets_All_Points()
    {
        var (vm, _, s) = Make(connected: true);
        vm.ApplyPreset(new PedalCurvePreset("Zero", new double[] { 0, 0, 0, 0, 0, 0 }));
        Assert.All(vm.Points, p => Assert.Equal(0, p.Value));
        s.Dispose();
    }

    [Fact]
    public void Telemetry_Updates_Live_Values()
    {
        var (vm, t, s) = Make(connected: true);
        t.Emit(new PedalState { Brake = new PedalReading(2048, 32768) });
        Assert.InRange(vm.CurrentOutput01, 0.49, 0.51);
        Assert.Single(vm.LiveValues);
        s.Dispose();
    }

    [Fact]
    public void Presets_Include_Linear()
    {
        Assert.Contains(PedalCurvePresets.All, p => p.Name == "Linear");
    }
}
