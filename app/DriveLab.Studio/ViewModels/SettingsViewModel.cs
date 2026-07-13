using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly DeviceSession _session;

    public IReadOnlyList<SettingFieldViewModel> BasicFields { get; }
    public IReadOnlyList<SettingFieldViewModel> AdvancedFields { get; }
    public IReadOnlyList<SettingFieldViewModel> HardwareFields { get; }

    public SettingsViewModel(DeviceSession session)
    {
        _session = session;

        List<SettingFieldViewModel> ForTab(SettingTab tab) =>
            SettingsSchema.All.Where(d => d.Tab == tab)
                .Select(d => new SettingFieldViewModel(session, d))
                .ToList();

        BasicFields = ForTab(SettingTab.Basic);
        AdvancedFields = ForTab(SettingTab.Advanced);
        HardwareFields = ForTab(SettingTab.Hardware);

        _session.Connected += OnConnected;
    }

    public async Task LoadAsync()
    {
        foreach (var field in BasicFields.Concat(AdvancedFields).Concat(HardwareFields))
            await field.LoadAsync();
    }

    private void OnConnected(object? sender, EventArgs e) => _ = LoadAsync();

    public override void Dispose()
    {
        _session.Connected -= OnConnected;
        base.Dispose();
    }
}
