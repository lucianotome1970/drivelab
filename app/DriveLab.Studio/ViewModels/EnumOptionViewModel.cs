using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DriveLab.Studio.ViewModels;

/// <summary>Um chip de opção (enum) abaixo do slider, ex.: tipo de encoder.</summary>
public sealed partial class EnumOptionViewModel : ObservableObject
{
    private readonly Action _select;

    public int Value { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SelectCommand))]
    private bool _canSelect;

    public EnumOptionViewModel(int value, string label, Action select)
    {
        Value = value;
        Label = label;
        _select = select;
    }

    [RelayCommand(CanExecute = nameof(CanSelect))]
    private void Select() => _select();
}
