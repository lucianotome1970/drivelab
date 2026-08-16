// ============================================================================
//  DriveLab
//  App.axaml.cs — Code-behind do App: exibe o splash, detecta dispositivos e inicializa a janela principal.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Markup.Xaml;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;
using DriveLab.Studio.Views;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio;

public partial class App : Application
{
    /// <summary>Preferências de quem usa: iniciar com o Windows e continuar na bandeja ao fechar.
    /// Pública porque a tela as edita e o fechamento da janela as consulta.</summary>
    public static AppPreferences Preferencias { get; } = new();

    private static TrayIcon? _bandeja;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var simulatorMode = CompositionRoot.IsSimulatorRequested(desktop.Args);
            var advancedMode = CompositionRoot.IsAdvancedMode(desktop.Args);

            var splashVm = new SplashViewModel();
            var splash = new SplashWindow { DataContext = splashVm };
            splash.Show();
            splash.Activate(); // pede foreground: sem isso o SO às vezes deixa o splash atrás

            _ = RunStartupAsync(desktop, splash, splashVm, simulatorMode, advancedMode);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunStartupAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash,
        SplashViewModel splashVm,
        bool simulatorMode,
        bool advancedMode)
    {
        try
        {
            var detector = CompositionRoot.CreateStartupDetector(simulatorMode);
            var progress = new Progress<StartupProgress>(p =>
            {
                splashVm.Progress = p.Fraction;
                splashVm.Status = p.Status;
            });
            await detector.RunAsync(progress);
        }
        catch
        {
            // Falha na detecção não deve travar o app no splash: segue para abri-lo.
        }

        IBaseTransport? transport = simulatorMode ? null : CompositionRoot.CreateHidTransport();
        var viewModel = CompositionRoot.CreateMainWindowViewModel(transport, simulatorMode, advancedMode);
        var main = new MainWindow { DataContext = viewModel };
        desktop.MainWindow = main;
        desktop.Exit += (_, _) => viewModel.Dispose();

        // ⚠️ O APP PRECISA SOBREVIVER AO FECHAR DA JANELA. Os atalhos de centralizar — tecla, botão
        // do aro — vivem AQUI: é o Studio que escuta o teclado, lê os botões e manda o comando para a
        // base. Com o app encerrado ninguém está escutando, e o atalho deixa de existir.
        //
        // Com OnLastWindowClose (o padrão), esconder a janela encerraria o processo — então a opção
        // muda para encerrar só quando alguém pedir explicitamente, pela bandeja ou pelo botão de
        // sair. Quem desliga "manter na bandeja" volta a ter o X encerrando de verdade (ver
        // MainWindow.OnClosing).
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        Preferencias.Load();
        if (OperatingSystem.IsWindows()) WindowsStartup.Aplicar(Preferencias.IniciarComWindows);
        CriarBandeja(desktop);

        main.Show();
        main.Activate(); // garante que a janela principal fica em foreground ao fechar o splash
        splash.Close();
    }

    /// <summary>Ícone da bandeja: mostra que o app continua vivo e dá a saída para encerrar.
    ///
    /// <para>⚠️ COM ÍCONE, e não invisível. Um programa que inicia sozinho, escuta o teclado e não
    /// aparece em lugar nenhum tem exatamente o comportamento de um keylogger — antivírus implicam,
    /// e com razão. O ícone é o que torna isso honesto: quem usa vê que está rodando e consegue
    /// fechar.</para></summary>
    private static void CriarBandeja(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (_bandeja is not null) return;

        var abrir = new NativeMenuItem(L.Get("Tray_Open"));
        abrir.Click += (_, _) => MostrarJanela(desktop);

        var sair = new NativeMenuItem(L.Get("Tray_Quit"));
        sair.Click += (_, _) => desktop.Shutdown();

        _bandeja = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://DriveLab.Studio/Assets/logo.png"))),
            ToolTipText = "DriveLab Studio",
            Menu = new NativeMenu { Items = { abrir, sair } },
        };
        // Clique no ícone traz a janela de volta — é o que todo mundo tenta primeiro, antes do menu.
        _bandeja.Clicked += (_, _) => MostrarJanela(desktop);

        TrayIcon.SetIcons(Current!, new TrayIcons { _bandeja });
    }

    private static void MostrarJanela(IClassicDesktopStyleApplicationLifetime desktop)
    {
        if (desktop.MainWindow is not { } w) return;
        w.Show();
        w.WindowState = WindowState.Normal;
        w.Activate();
    }
}
