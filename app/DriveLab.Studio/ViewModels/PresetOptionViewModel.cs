// ============================================================================
//  DriveLab
//  PresetOptionViewModel.cs — Chip de preset (valor rápido) selecionável abaixo de um slider.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DriveLab.Studio.ViewModels;

/// <summary>Um chip de preset (valor rápido) abaixo do slider.</summary>
public sealed partial class PresetOptionViewModel : ObservableObject
{
    private readonly Action _select;

    public int Value { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private bool _canSelect;

    public PresetOptionViewModel(int value, Action select)
    {
        Value = value;
        _select = select;
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void Select() => _select();
}
