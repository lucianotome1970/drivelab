using CommunityToolkit.Mvvm.ComponentModel;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.ViewModels;

public partial class SplashViewModel : ViewModelBase
{
    /// <summary>Progresso da detecção, 0..1 (ligado à ProgressBar, Maximum=1).</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = L.Get("Splash_Starting");

    public string VersionText => "DRIVELAB APP · v1.0.0";
}
