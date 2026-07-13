using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;
using DriveLab.Studio.ViewModels;
using DriveLab.Studio.Views;

namespace DriveLab.Studio;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var simulatorMode = CompositionRoot.IsSimulatorRequested(desktop.Args);

            var splashVm = new SplashViewModel();
            var splash = new SplashWindow { DataContext = splashVm };
            splash.Show();

            _ = RunStartupAsync(desktop, splash, splashVm, simulatorMode);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunStartupAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash,
        SplashViewModel splashVm,
        bool simulatorMode)
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

        ITransport? transport = simulatorMode ? null : CompositionRoot.CreateHidTransport();
        var viewModel = CompositionRoot.CreateMainWindowViewModel(transport, simulatorMode);
        var main = new MainWindow { DataContext = viewModel };
        desktop.MainWindow = main;
        desktop.Exit += (_, _) => viewModel.Dispose();
        main.Show();
        splash.Close();
    }
}
