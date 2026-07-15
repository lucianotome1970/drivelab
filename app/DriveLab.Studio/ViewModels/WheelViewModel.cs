// ============================================================================
//  DriveLab
//  WheelViewModel.cs — VM da tela do volante (mock): cores de LED por botão e configuração de pás.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

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

    /// <summary>Só em modo /simulator o clique simula pressionar (acende). Em modo real o "aceso"
    /// virá da telemetria do firmware (via SetControlPressed), não do mouse.</summary>
    public bool IsSimulator { get; }

    /// <summary>Botões do desenho do volante (coloríveis + pressionáveis). Os giratórios NÃO entram
    /// aqui: são encoders (giram, não clicam) — o desenho já os mostra; girar fica p/ o futuro.</summary>
    public IReadOnlyList<WheelButtonViewModel> Buttons { get; }

    // Pás (no painel, não no desenho) — pressionáveis (acendem). Propriedades nomeadas (não índice)
    // para as bindings compiladas resolverem sem problema.
    public WheelButtonViewModel ShiftDown { get; } = new("ShiftDown", 0, 0, "#FF9F0A");
    public WheelButtonViewModel ShiftUp { get; } = new("ShiftUp", 0, 0, "#FF9F0A");
    public WheelButtonViewModel ClutchLeft { get; } = new("ClutchLeft", 0, 0, "#FF9F0A");
    public WheelButtonViewModel ClutchRight { get; } = new("ClutchRight", 0, 0, "#FF9F0A");

    /// <summary>As 4 pás (p/ o lookup do SetControlPressed).</summary>
    public IReadOnlyList<WheelButtonViewModel> Paddles => new[] { ShiftDown, ShiftUp, ClutchLeft, ClutchRight };

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

    public WheelViewModel(IWheelProfileStorage storage, bool simulatorMode = false)
    {
        _storage = storage;
        IsSimulator = simulatorMode;
        // Nome, posição normalizada (0-1 sobre a imagem quadrada wheel.png), cor padrão.
        Buttons = new List<WheelButtonViewModel>
        {
            new("N",     0.222, 0.348, "#BF5AF2"),
            new("PIT",   0.293, 0.352, "#FFD60A"),
            new("DRS",   0.718, 0.348, "#34C759"),
            new("KILL",  0.786, 0.346, "#FF3B30"),
            new("RADIO", 0.256, 0.452, "#32ADE6"),
            new("TC",    0.256, 0.520, "#FFD60A"),
            new("MENU",  0.744, 0.452, "#FF9F0A"),
            new("ESC",   0.744, 0.520, "#32ADE6"),
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
