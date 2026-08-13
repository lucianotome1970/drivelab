// ============================================================================
//  DriveLab
//  MainWindowViewModel.cs — VM da janela principal: navegação entre páginas, conexão e modo simulador.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
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

    /// <summary>Parada de emergência sempre acessível no topo (corta a força da base).</summary>
    public EmergencyStopViewModel EStop { get; }
    public bool SimulatorMode { get; }
    public object CurrentPage => SelectedPage.Page;
    public string Title => "DriveLab Studio";

    // Topo do app: "DriveLab Studio" na Home; "DriveLab Studio — <título do módulo>" nos módulos.
    // O título deixa de ser repetido dentro de cada card.
    public string HeaderTitle => string.IsNullOrEmpty(SelectedPage.Title)
        ? "DriveLab Studio"
        : $"DriveLab Studio — {SelectedPage.Title}";

    public MainWindowViewModel(BaseSession session, ConnectionViewModel connection, IReadOnlyList<NavItem> pages, TestViewModel test, bool simulatorMode = false, IReadOnlyList<IDisposable>? autoConnectors = null)
    {
        _session = session;
        Connection = connection;
        Pages = pages;
        Test = test;
        SimulatorMode = simulatorMode;
        _autoConnectors = autoConnectors ?? Array.Empty<IDisposable>();
        _selectedPage = pages[0];
        EStop = new EmergencyStopViewModel(session);

        // VERSÕES — a pergunta que isto responde é "preciso atualizar?", e ela só tem resposta com
        // os DOIS lados na tela: o app e a placa sobem juntos numa release, e o caso que morde é
        // atualizar um e esquecer o outro. Até 2026-08-13 os dois números nem batiam entre si (app
        // 0.1.5, firmware 0.4.0, release 0.2.3) e nenhum servia para decidir nada.
        session.StateReceived += AoReceberEstadoParaVersao;
    }

    /// <summary>Versão do app, do assembly (`&lt;Version&gt;` no csproj); sem o sufixo "+hash".</summary>
    public static string AppVersion { get; } =
        (System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? "0.0.0").Split('+')[0];

    /// <summary>Versão do firmware da base, vinda da telemetria. "—" enquanto não houver base.</summary>
    [ObservableProperty] private string _baseVersion = "—";

    /// <summary>True quando app e base divergem — é o único estado que pede ação de quem usa.</summary>
    [ObservableProperty] private bool _versionMismatch;

    /// <summary>O que aparece na barra lateral. Curto porque a barra tem 64 px — o detalhe fica no
    /// tooltip, que é onde cabe a frase inteira.</summary>
    public string AppVersionShort => "v" + AppVersion;
    public string BaseVersionShort => BaseVersion == "—" ? "base —" : "base " + BaseVersion;

    public string VersionTooltip
    {
        get
        {
            var nl = System.Environment.NewLine;
            if (BaseVersion == "—")
                return $"DriveLab Studio {AppVersion}{nl}Base não conectada";

            var cabecalho = $"DriveLab Studio {AppVersion}{nl}Firmware da base {BaseVersion}{nl}{nl}";
            return VersionMismatch
                ? cabecalho + "As versões DIVERGEM — atualize o firmware pela aba de atualização, ou o app pela release."
                : cabecalho + "App e base na mesma versão.";
        }
    }

    private void AoReceberEstadoParaVersao(object? s, BaseState e)
    {
        var fw = $"{e.Firmware.Major}.{e.Firmware.Minor}.{e.Firmware.Patch}";
        if (fw == BaseVersion) return;
        BaseVersion = fw;
        OnPropertyChanged(nameof(BaseVersionShort));
        OnPropertyChanged(nameof(VersionTooltip));
        // "0.0.0" é firmware que não informa versão — não acusamos divergência sem saber.
        VersionMismatch = fw != "0.0.0" && fw != AppVersion;
        OnPropertyChanged(nameof(VersionTooltip));
    }

    partial void OnSelectedPageChanged(NavItem value)
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(HeaderTitle));
    }

    [RelayCommand]
    private void Navigate(NavItem item) => SelectedPage = item;

    public override void Dispose()
    {
        _session.StateReceived -= AoReceberEstadoParaVersao;
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
