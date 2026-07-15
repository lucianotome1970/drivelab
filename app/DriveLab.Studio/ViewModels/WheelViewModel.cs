using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Tela do volante (mock): cores de LED por botão + config de pás. Persistência em
/// JSON local; NADA vai ao dispositivo (firmware não expõe LED/pás ainda). O ponto de escrita
/// ao device entraria em SaveCommand quando o protocolo existir.</summary>
public partial class WheelViewModel : ViewModelBase
{
    private readonly IWheelProfileStorage _storage;

    public IReadOnlyList<WheelButtonViewModel> Buttons { get; }
    public IReadOnlyList<string> Palette { get; } = new[]
    {
        "#000000", "#FFFFFF", "#FF3B30", "#34C759", "#0A84FF",
        "#32ADE6", "#FFD60A", "#FF9F0A", "#BF5AF2", "#FF2D92",
    };
    public PaddlePairViewModel BottomPair { get; } = new();

    [ObservableProperty] private WheelButtonViewModel? _selectedButton;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBottomPair))]
    private int _paddleCount = 4;

    public bool ShowBottomPair => PaddleCount == 4;

    public WheelViewModel(IWheelProfileStorage storage)
    {
        _storage = storage;
        // Nome, posição normalizada (0-1 sobre a imagem quadrada), cor padrão.
        Buttons = new List<WheelButtonViewModel>
        {
            new("N",     0.22, 0.33, "#BF5AF2"),
            new("PIT",   0.29, 0.34, "#FFD60A"),
            new("DRS",   0.71, 0.34, "#34C759"),
            new("KILL",  0.78, 0.33, "#FF3B30"),
            new("RADIO", 0.25, 0.45, "#32ADE6"),
            new("TC",    0.25, 0.51, "#FFD60A"),
            new("MENU",  0.75, 0.45, "#FF9F0A"),
            new("ESC",   0.75, 0.51, "#32ADE6"),
        };

        // Carrega o perfil salvo no arranque (config persiste entre execuções, como MOZA).
        // Fire-and-forget: LoadAsync tolera arquivo ausente (mantém os defaults acima).
        _ = LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void SelectButton(WheelButtonViewModel button)
    {
        foreach (var b in Buttons)
            b.IsSelected = ReferenceEquals(b, button);
        SelectedButton = button;
    }

    [RelayCommand]
    private void SetColor(string hex)
    {
        if (SelectedButton is not null)
            SelectedButton.ColorHex = hex;
    }

    [RelayCommand]
    private void SetPaddleCount(string count) => PaddleCount = int.Parse(count);

    [RelayCommand]
    private Task Save() => _storage.SaveAsync(Export());
    // (Ponto futuro: quando o firmware expuser LED/pás, escrever também no device aqui.)

    [RelayCommand]
    private async Task Load()
    {
        var profile = await _storage.LoadAsync();
        if (profile is null)
            return;
        Apply(profile);
    }

    private WheelProfile Export() => new(
        Buttons.Select(b => new WheelButtonColor(b.Name, b.ColorHex)).ToArray(),
        PaddleCount,
        BottomPair.Function, BottomPair.Mode, BottomPair.Actuation, BottomPair.BitePoint);

    private void Apply(WheelProfile p)
    {
        foreach (var saved in p.Buttons)
        {
            var b = Buttons.FirstOrDefault(x => x.Name == saved.Name);
            if (b is not null)
                b.ColorHex = saved.ColorHex;
        }
        PaddleCount = p.PaddleCount;
        BottomPair.Function = p.BottomFunction;
        BottomPair.Mode = p.BottomMode;
        BottomPair.Actuation = p.BottomActuation;
        BottomPair.BitePoint = p.BottomBitePoint;
    }
}
