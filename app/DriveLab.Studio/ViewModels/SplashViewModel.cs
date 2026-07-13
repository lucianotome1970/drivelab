using CommunityToolkit.Mvvm.ComponentModel;

namespace DriveLab.Studio.ViewModels;

public partial class SplashViewModel : ViewModelBase
{
    /// <summary>Progresso da detecção, 0..1 (ligado à ProgressBar, Maximum=1).</summary>
    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = "Iniciando…";

    public string VersionText => "DRIVELAB APP · v1.0.0";
}
