using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly DeviceSession _session;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private double _angleDegrees;

    [ObservableProperty]
    private double _positionPercent;

    [ObservableProperty]
    private int _motionRange = 900;

    public DashboardViewModel(DeviceSession session)
    {
        _session = session;
        _session.StateReceived += OnState;
    }

    private void OnState(object? sender, DeviceState state)
    {
        AngleDegrees = state.AngleDeciDeg / 10.0;
        PositionPercent = state.Position / 100.0;
        IsConnected = _session.IsConnected;
    }

    [RelayCommand]
    private Task CenterAsync() => _session.SendCommandAsync(DeviceCommand.ResetCenter);

    [RelayCommand]
    private async Task SetMaxAngleAsync(string degrees)
    {
        var value = int.Parse(degrees, CultureInfo.InvariantCulture);
        await _session.WriteSettingAsync(SettingId.MotionRange, new SettingValue(SettingType.UInt16, value));
        MotionRange = value;
    }
}
