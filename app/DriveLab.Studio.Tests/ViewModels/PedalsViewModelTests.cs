using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class PedalsViewModelTests
{
    private sealed class FakeStorage : IPedalProfileStorage
    {
        public PedalProfile? Saved;
        public PedalProfile? ToLoad;
        public Task SaveAsync(PedalProfile profile) { Saved = profile; return Task.CompletedTask; }
        public Task<PedalProfile?> LoadAsync() => Task.FromResult(ToLoad);
    }

    private static (PedalsViewModel vm, FakePedalTransport t, FakeStorage store) Make()
    {
        var t = new FakePedalTransport();
        var s = new PedalDeviceSession(t, new ImmediateUiDispatcher());
        var store = new FakeStorage();
        return (new PedalsViewModel(s, store), t, store);
    }

    [Fact]
    public void Has_Three_Columns_And_Presets()
    {
        var (vm, _, _) = Make();
        Assert.Equal(3, vm.Columns.Count);
        Assert.Equal(PedalIndex.Clutch, vm.Columns[0].Pedal);
        Assert.Equal(PedalIndex.Throttle, vm.Columns[2].Pedal);
        Assert.NotEmpty(vm.Presets);
        vm.Dispose();
    }

    [Fact]
    public async Task Connect_Sets_IsConnected()
    {
        var (vm, _, _) = Make();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(vm.IsConnected);
        await vm.DisconnectCommand.ExecuteAsync(null);
        Assert.False(vm.IsConnected);
        vm.Dispose();
    }

    [Fact]
    public async Task SaveToController_Sends_SaveToFlash_When_Connected()
    {
        var (vm, t, _) = Make();
        await vm.ConnectCommand.ExecuteAsync(null);
        await vm.SaveToControllerCommand.ExecuteAsync(null);
        Assert.Equal(PedalCommandId.SaveToFlash, t.LastCommand!.Value.cmd);
        vm.Dispose();
    }

    [Fact]
    public async Task SavePreferences_Exports_Current_State()
    {
        var (vm, _, store) = Make();
        await vm.ConnectCommand.ExecuteAsync(null);
        vm.Columns[1].Points[3].Value = 42;
        await vm.SavePreferencesCommand.ExecuteAsync(null);
        Assert.NotNull(store.Saved);
        Assert.Equal(3, store.Saved!.Columns.Length);
        Assert.Equal(42, store.Saved.Columns[1].Curve[3]);
        vm.Dispose();
    }

    [Fact]
    public async Task LoadPreferences_Applies_Profile_To_Columns()
    {
        var (vm, _, store) = Make();
        await vm.ConnectCommand.ExecuteAsync(null);
        store.ToLoad = new PedalProfile(new[]
        {
            new PedalProfileColumn(0, 0, 4095, false, 0, new double[] { 0, 0, 0, 0, 0, 0 }, 1000),
            new PedalProfileColumn(0, 0, 4095, false, 0, new double[] { 0, 10, 20, 30, 40, 50 }, 1000),
            new PedalProfileColumn(0, 0, 4095, false, 0, new double[] { 0, 20, 40, 60, 80, 100 }, 1000),
        });
        await vm.LoadPreferencesCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.Columns[0].Points[5].Value);
        Assert.Equal(50, vm.Columns[1].Points[5].Value);
        vm.Dispose();
    }

    [Fact]
    public void SelectedPreset_Applies_To_All_Columns()
    {
        var (vm, _, _) = Make();
        vm.SelectedPreset = new PedalCurvePreset("Zero", new double[] { 0, 0, 0, 0, 0, 0 });
        Assert.All(vm.Columns, c => Assert.Equal(0, c.Points[5].Value));
        vm.Dispose();
    }

    [Fact]
    public async Task Telemetry_Appends_To_Combined_Samples()
    {
        var (vm, t, _) = Make();
        await vm.ConnectCommand.ExecuteAsync(null);
        t.Emit(new PedalState
        {
            Clutch = new PedalReading(0, 0),
            Brake = new PedalReading(0, 32768),
            Throttle = new PedalReading(0, 65535),
        });
        Assert.Single(vm.BrakeSamples);
        Assert.Equal(3, vm.CombinedSeries.Length);
        vm.Dispose();
    }
}
