// ============================================================================
//  DriveLab
//  PaddlePairViewModel.cs — VM do par de pás de baixo (embreagem/livre/botão), mock persistido em JSON local.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DriveLab.Studio.ViewModels;

/// <summary>Configuração do par de pás de baixo (embreagem/livre/botão). Mock — persistido
/// em JSON local, nada vai ao dispositivo.</summary>
public partial class PaddlePairViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMode))]
    [NotifyPropertyChangedFor(nameof(ShowBitePoint))]
    private PaddleFunction _function = PaddleFunction.Clutch;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBitePoint))]
    private PaddleMode _mode = PaddleMode.Combined;

    [ObservableProperty]
    private PaddleActuation _actuation = PaddleActuation.Digital;

    [ObservableProperty]
    private int _bitePoint = 50;

    /// <summary>Modo (combinada/independente) só faz sentido em embreagem.</summary>
    public bool ShowMode => Function == PaddleFunction.Clutch;

    /// <summary>Bite point só na dupla embreagem combinada.</summary>
    public bool ShowBitePoint => Function == PaddleFunction.Clutch && Mode == PaddleMode.Combined;

    [RelayCommand] private void SetFunction(string f) => Function = Enum.Parse<PaddleFunction>(f);
    [RelayCommand] private void SetMode(string m) => Mode = Enum.Parse<PaddleMode>(m);
    [RelayCommand] private void SetActuation(string a) => Actuation = Enum.Parse<PaddleActuation>(a);
}
