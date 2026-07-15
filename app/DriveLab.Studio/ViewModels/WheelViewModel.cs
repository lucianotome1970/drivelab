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

    /// <summary>Controles no desenho do volante: 8 botões + 5 giratórios (coloríveis + pressionáveis).</summary>
    public IReadOnlyList<WheelButtonViewModel> Buttons { get; }

    /// <summary>Pás (fora do desenho — no painel): [0]=marcha↓ [1]=marcha↑ [2]=embreagem E [3]=embreagem D.
    /// Pressionáveis (acendem), não coloríveis.</summary>
    public IReadOnlyList<WheelButtonViewModel> Paddles { get; }

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
        // Nome, posição normalizada (0-1 sobre a imagem quadrada), cor padrão, diâmetro do marcador.
        Buttons = new List<WheelButtonViewModel>
        {
            // Botões (marcador pequeno)
            new("N",     0.22, 0.33, "#BF5AF2"),
            new("PIT",   0.29, 0.34, "#FFD60A"),
            new("DRS",   0.71, 0.34, "#34C759"),
            new("KILL",  0.78, 0.33, "#FF3B30"),
            new("RADIO", 0.25, 0.45, "#32ADE6"),
            new("TC",    0.25, 0.51, "#FFD60A"),
            new("MENU",  0.75, 0.45, "#FF9F0A"),
            new("ESC",   0.75, 0.51, "#32ADE6"),
            // Giratórios (marcador maior) — cores dos anéis do desenho
            new("BRAKE BIAS", 0.30, 0.62, "#FF9F0A", 40),
            new("MAP",        0.38, 0.71, "#34C759", 40),
            new("FUEL",       0.50, 0.73, "#FF9F0A", 40),
            new("BOOST",      0.62, 0.71, "#0A84FF", 40),
            new("ABS",        0.71, 0.62, "#FF9F0A", 40),
        };

        // Pás (no painel, não no desenho) — acendem ao pressionar, cor de acento.
        Paddles = new List<WheelButtonViewModel>
        {
            new("ShiftDown",   0, 0, "#FF9F0A"),
            new("ShiftUp",     0, 0, "#FF9F0A"),
            new("ClutchLeft",  0, 0, "#FF9F0A"),
            new("ClutchRight", 0, 0, "#FF9F0A"),
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

    /// <summary>Acende/apaga um controle pelo nome. Caminho ÚNICO do "pressionar":
    /// hoje chamado pela simulação (segurar o mouse); no futuro, pelo evento de telemetria
    /// de botões do firmware do volante (quando o A0 expuser) — mesmo efeito visual.</summary>
    public void SetControlPressed(string name, bool pressed)
    {
        var control = Buttons.FirstOrDefault(b => b.Name == name)
                      ?? Paddles.FirstOrDefault(p => p.Name == name);
        if (control is not null)
            control.IsPressed = pressed;
    }

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
