// ============================================================================
//  DriveLab
//  SplashViewModel.cs — VM da tela de splash: progresso e status da detecção de módulos.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

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
