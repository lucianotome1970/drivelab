// ============================================================================
//  DriveLab
//  WheelViewModel.cs — VM da tela do volante (mock): cores de LED por botão e configuração de pás.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Tela do volante: cores de LED por botão + config de pás. Em modo real conecta ao aro
/// (<see cref="WheelDeviceSession"/>): a telemetria acende os botões pressionados e as cores são
/// enviadas AO VIVO ao dispositivo. Sem sessão (simulador), vira o mock com persistência JSON local.</summary>
public partial class WheelViewModel : ViewModelBase
{
    private readonly IWheelProfileStorage _storage;
    private readonly WheelDeviceSession? _session;

    /// <summary>Só em modo /simulator o clique simula pressionar (acende). Em modo real o "aceso"
    /// vem da telemetria do firmware (via SetControlPressed), não do mouse.</summary>
    public bool IsSimulator { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private bool _isConnected;

    /// <summary>Brilho global dos LEDs enviado no report (0..255). Empurra ao vivo ao mudar.</summary>
    [ObservableProperty] private byte _ledBrightness = 200;

    /// <summary>Rótulo da origem (ex.: "Volante detectado") p/ o selo de status; vazio se não houver sessão.</summary>
    public string SourceLabel { get; }

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

    public WheelViewModel(IWheelProfileStorage storage, bool simulatorMode = false, WheelDeviceSession? session = null)
    {
        _storage = storage;
        IsSimulator = simulatorMode;
        _session = session;
        SourceLabel = session?.SourceLabel ?? "";
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

        if (_session is not null)
        {
            _isConnected = _session.IsConnected;
            _session.StateReceived += OnState;
            _session.Connected += OnConnectionChanged;
            _session.Disconnected += OnConnectionChanged;
        }

        // Carrega o perfil salvo no arranque (config persiste entre execuções, como MOZA).
        // Fire-and-forget: LoadAsync tolera arquivo ausente (mantém os defaults acima).
        _ = LoadCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (_session is null) return;
        await _session.ConnectAsync();
        PushLeds();   // manda as cores atuais assim que conecta
    }

    private bool CanConnect() => _session is not null && !IsConnected;

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task DisconnectAsync() => _session?.DisconnectAsync() ?? Task.CompletedTask;

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        IsConnected = _session?.IsConnected ?? false;
        if (IsConnected) PushLeds();
    }

    // Telemetria do aro → visual de pressão (mesmo caminho da simulação).
    // Mapa provisório: bits 0..7 = os 8 botões do desenho; bits 10/11 = marcha ↓/↑;
    // embreagens acendem pelo eixo (output alto). Ajustável quando o layout físico fechar.
    private void OnState(object? sender, WheelState s)
    {
        for (var i = 0; i < Buttons.Count && i < 32; i++)
            Buttons[i].IsPressed = s.IsButtonPressed(i);
        ShiftDown.IsPressed = s.IsButtonPressed(10);
        ShiftUp.IsPressed   = s.IsButtonPressed(11);
        ClutchLeft.IsPressed  = s.ClutchLeft.Output  > 32768;
        ClutchRight.IsPressed = s.ClutchRight.Output > 32768;
        IsConnected = _session?.IsConnected ?? false;
    }

    /// <summary>Monta o WheelLed com as cores dos botões (pixels 0..N) e envia ao vivo.</summary>
    private void PushLeds()
    {
        if (_session is null || !_session.IsConnected)
            return;
        var colors = Buttons.Select(b => HexToColor(b.ColorHex)).ToArray();
        _ = _session.SendLedAsync(new WheelLedReport(LedBrightness, colors));
    }

    partial void OnLedBrightnessChanged(byte value) => PushLeds();

    private static WheelLedColor HexToColor(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length != 6 || !uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            return new WheelLedColor(0, 0, 0);
        return new WheelLedColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
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
        {
            SelectedButton.ColorHex = hex;
            PushLeds();   // ao vivo: reflete a cor no aro na hora
        }
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
        PushLeds();   // perfil carregado → reflete no aro (se conectado)
    }

    public override void Dispose()
    {
        if (_session is not null)
        {
            _session.StateReceived -= OnState;
            _session.Connected -= OnConnectionChanged;
            _session.Disconnected -= OnConnectionChanged;
            _session.Dispose();
        }
        base.Dispose();
    }
}
