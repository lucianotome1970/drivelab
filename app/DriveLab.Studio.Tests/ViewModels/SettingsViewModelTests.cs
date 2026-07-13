using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class SettingsViewModelTests
{
    private static SettingsViewModel New()
    {
        var session = new DeviceSession(new FakeTransport(), new ImmediateUiDispatcher());
        return new SettingsViewModel(session);
    }

    [Fact]
    public void Fields_Are_Grouped_By_Tab()
    {
        var vm = New();
        Assert.Equal(6, vm.BasicFields.Count);
        Assert.Equal(6, vm.AdvancedFields.Count);
        Assert.Equal(6, vm.HardwareFields.Count);
    }

    [Fact]
    public async Task LoadAsync_Populates_Field_Values_From_Device()
    {
        var vm = New();
        await vm.LoadAsync();
        // FakeTransport.ReadSettingAsync returns 900 for every field
        Assert.All(vm.BasicFields, f => Assert.Equal(900, f.Value));
    }
}
