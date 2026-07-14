using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Studio.Services;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.ViewModels;

public partial class ConnectionViewModel : ViewModelBase
{
    private readonly DeviceSession _session;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusText = L.Get("Status_Disconnected");

    public ConnectionViewModel(DeviceSession session) => _session = session;

    [RelayCommand]
    private async Task ConnectAsync()
    {
        await _session.ConnectAsync();
        IsConnected = _session.IsConnected;
        StatusText = IsConnected ? L.Get("Status_Connected") : L.Get("Status_NoDevice");
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _session.DisconnectAsync();
        IsConnected = _session.IsConnected;
        StatusText = L.Get("Status_Disconnected");
    }
}
