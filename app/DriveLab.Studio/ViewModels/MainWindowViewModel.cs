using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly DeviceSession _session;

    [ObservableProperty]
    private NavItem _selectedPage;

    public ConnectionViewModel Connection { get; }
    public IReadOnlyList<NavItem> Pages { get; }
    public object CurrentPage => SelectedPage.Page;
    public string Title => "DriveLab Studio";

    public MainWindowViewModel(DeviceSession session, ConnectionViewModel connection, IReadOnlyList<NavItem> pages)
    {
        _session = session;
        Connection = connection;
        Pages = pages;
        _selectedPage = pages[0];
    }

    partial void OnSelectedPageChanged(NavItem value) => OnPropertyChanged(nameof(CurrentPage));

    [RelayCommand]
    private void Navigate(NavItem item) => SelectedPage = item;

    public override void Dispose()
    {
        foreach (var page in Pages)
            page.Page.Dispose();
        _session.Dispose();
        base.Dispose();
    }
}
