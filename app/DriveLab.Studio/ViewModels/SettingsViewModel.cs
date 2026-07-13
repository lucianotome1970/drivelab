using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public IReadOnlyList<SettingFieldViewModel> BasicFields { get; }
    public IReadOnlyList<SettingFieldViewModel> AdvancedFields { get; }
    public IReadOnlyList<SettingFieldViewModel> HardwareFields { get; }

    public SettingsViewModel(DeviceSession session)
    {
        List<SettingFieldViewModel> ForTab(SettingTab tab) =>
            SettingsSchema.All.Where(d => d.Tab == tab)
                .Select(d => new SettingFieldViewModel(session, d))
                .ToList();

        BasicFields = ForTab(SettingTab.Basic);
        AdvancedFields = ForTab(SettingTab.Advanced);
        HardwareFields = ForTab(SettingTab.Hardware);
    }

    public async Task LoadAsync()
    {
        foreach (var field in BasicFields.Concat(AdvancedFields).Concat(HardwareFields))
            await field.LoadAsync();
    }

    [RelayCommand]
    private Task Load() => LoadAsync();
}
