using CommunityToolkit.Mvvm.ComponentModel;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public partial class SettingFieldViewModel : ViewModelBase
{
    private readonly DeviceSession _session;
    private readonly SettingDescriptor _descriptor;
    private bool _loading;

    [ObservableProperty]
    private double _value;

    public string DisplayName => _descriptor.DisplayName;
    public double Min => _descriptor.Min;
    public double Max => _descriptor.Max;
    public string Unit => _descriptor.Unit;

    public SettingFieldViewModel(DeviceSession session, SettingDescriptor descriptor)
    {
        _session = session;
        _descriptor = descriptor;
        _value = descriptor.Default;
    }

    public async Task LoadAsync()
    {
        var value = await _session.ReadSettingAsync(_descriptor.Id);
        _loading = true;
        Value = value.AsDouble;
        _loading = false;
    }

    public Task WriteAsync() =>
        _session.WriteSettingAsync(_descriptor.Id, new SettingValue(_descriptor.Type, _descriptor.Clamp(Value)));

    partial void OnValueChanged(double value)
    {
        if (!_loading)
            _ = WriteAsync();
    }
}
