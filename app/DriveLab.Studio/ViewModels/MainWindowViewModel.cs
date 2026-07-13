using CommunityToolkit.Mvvm.ComponentModel;

namespace DriveLab.Studio.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private object _currentPage;

    public ConnectionViewModel Connection { get; }
    public string Title => "DriveLab Studio";

    public MainWindowViewModel(ConnectionViewModel connection, DashboardViewModel dashboard)
    {
        Connection = connection;
        _currentPage = dashboard;
    }
}
