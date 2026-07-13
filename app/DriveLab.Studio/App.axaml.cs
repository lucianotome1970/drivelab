using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DriveLab.Studio.Views;

namespace DriveLab.Studio;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splash = new SplashWindow();
            splash.Show();

            DispatcherTimer.RunOnce(() =>
            {
                var main = new MainWindow
                {
                    DataContext = CompositionRoot.CreateMainWindowViewModel()
                };
                desktop.MainWindow = main;
                main.Show();
                splash.Close();
            }, TimeSpan.FromSeconds(2.2));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
