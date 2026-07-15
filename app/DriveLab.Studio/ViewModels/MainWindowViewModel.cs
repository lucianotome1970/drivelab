// ============================================================================
//  DriveLab
//  MainWindowViewModel.cs — VM da janela principal: navegação entre páginas, conexão e modo simulador.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly BaseSession _session;
    private readonly IReadOnlyList<IDisposable> _autoConnectors;

    [ObservableProperty]
    private NavItem _selectedPage;

    public ConnectionViewModel Connection { get; }
    public IReadOnlyList<NavItem> Pages { get; }
    public TestViewModel Test { get; }
    public bool SimulatorMode { get; }
    public object CurrentPage => SelectedPage.Page;
    public string Title => "DriveLab Studio";

    public MainWindowViewModel(BaseSession session, ConnectionViewModel connection, IReadOnlyList<NavItem> pages, TestViewModel test, bool simulatorMode = false, IReadOnlyList<IDisposable>? autoConnectors = null)
    {
        _session = session;
        Connection = connection;
        Pages = pages;
        Test = test;
        SimulatorMode = simulatorMode;
        _autoConnectors = autoConnectors ?? Array.Empty<IDisposable>();
        _selectedPage = pages[0];
    }

    partial void OnSelectedPageChanged(NavItem value) => OnPropertyChanged(nameof(CurrentPage));

    [RelayCommand]
    private void Navigate(NavItem item) => SelectedPage = item;

    public override void Dispose()
    {
        foreach (var connector in _autoConnectors)
            connector.Dispose();
        Connection.Dispose();
        foreach (var page in Pages)
            page.Page.Dispose();
        Test.Dispose();
        _session.Dispose();
        base.Dispose();
    }
}
