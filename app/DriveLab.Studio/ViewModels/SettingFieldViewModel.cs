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
    public bool IsInteger => _descriptor.Type != SettingType.Float;
    public string ValueText => IsInteger ? Value.ToString("0") : Value.ToString("0.##");

    public SettingFieldViewModel(DeviceSession session, SettingDescriptor descriptor)
    {
        _session = session;
        _descriptor = descriptor;
        _value = descriptor.Default;
        _session.SettingChanged += OnSettingChanged;
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Id != _descriptor.Id)
            return;

        // Atualiza sem disparar WriteAsync de volta (evita eco/loop).
        _loading = true;
        Value = e.Value.AsDouble;
        _loading = false;
    }

    public override void Dispose()
    {
        _session.SettingChanged -= OnSettingChanged;
        base.Dispose();
    }

    public async Task LoadAsync()
    {
        if (!_session.IsConnected)
            return;

        var value = await _session.ReadSettingAsync(_descriptor.Id);
        _loading = true;
        Value = value.AsDouble;
        _loading = false;
    }

    public Task WriteAsync()
    {
        if (!_session.IsConnected)
            return Task.CompletedTask;

        return _session.WriteSettingAsync(_descriptor.Id, new SettingValue(_descriptor.Type, _descriptor.Clamp(Value)));
    }

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(ValueText));
        if (!_loading)
            _ = WriteAsync();
    }
}
