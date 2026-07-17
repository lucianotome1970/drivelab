// ============================================================================
//  DriveLab
//  WheelViewModel.cs — VM da tela do volante (mock): cores de LED por botão e configuração de pás.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
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

    private bool _loading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveToControllerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetDefaultsCommand))]
    private bool _isConnected;

    /// <summary>Config alterada desde o último salvar/carregar — habilita "Salvar no controlador"
    /// (mesmo padrão de pedais/freio de mão). Zera ao salvar/carregar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveToControllerCommand))]
    private bool _isDirty;

    /// <summary>Brilho global dos LEDs enviado no report (0..255). Empurra ao vivo ao mudar.</summary>
    [ObservableProperty] private byte _ledBrightness = 200;

    /// <summary>Rótulo da origem (ex.: "Volante detectado") p/ o selo de status; vazio se não houver sessão.</summary>
    public string SourceLabel { get; }

    /// <summary>Botões do desenho do volante (coloríveis + pressionáveis). Os giratórios NÃO entram
    /// aqui: são encoders (giram, não clicam) — o desenho já os mostra; girar fica p/ o futuro.</summary>
    public IReadOnlyList<WheelButtonViewModel> Buttons { get; }

    // Pás sobrepostas no desenho do volante (atrás dos punhos): esq = marcha↓ + embreagem esq;
    // dir = marcha↑ + embreagem dir. Posições normalizadas sobre wheel.png (ajustáveis).
    public WheelButtonViewModel ShiftDown { get; }   = new("ShiftDown",   0.145, 0.40, "#FF9F0A");
    public WheelButtonViewModel ShiftUp { get; }     = new("ShiftUp",     0.855, 0.40, "#FF9F0A");
    public WheelButtonViewModel ClutchLeft { get; }  = new("ClutchLeft",  0.145, 0.53, "#FF9F0A");
    public WheelButtonViewModel ClutchRight { get; } = new("ClutchRight", 0.855, 0.53, "#FF9F0A");

    /// <summary>As 4 pás (p/ o lookup do SetControlPressed).</summary>
    public IReadOnlyList<WheelButtonViewModel> Paddles => new[] { ShiftDown, ShiftUp, ClutchLeft, ClutchRight };

    /// <summary>Knobs rotativos (potência) sobre o desenho: reagem ao girar (delta de encoder da
    /// telemetria). Posições medidas sobre o wheel.png; Glow pulsa e decai por frame.</summary>
    public IReadOnlyList<WheelButtonViewModel> Knobs { get; } = new List<WheelButtonViewModel>
    {
        new("BRAKE BIAS", 0.271, 0.618, "#FF6A00", diameter: 42),
        new("MAP",        0.375, 0.695, "#34C759", diameter: 42),
        new("FUEL",       0.503, 0.708, "#FF6A00", diameter: 42),
        new("BOOST",      0.631, 0.695, "#32ADE6", diameter: 42),
        new("ABS",        0.733, 0.618, "#FF6A00", diameter: 42),
    };

    /// <summary>Cores padrão de fábrica dos 8 botões (mesmas do construtor), para o "Padrão".</summary>
    private static readonly Dictionary<string, string> DefaultColors = new()
    {
        ["N"] = "#BF5AF2", ["PIT"] = "#FFD60A", ["DRS"] = "#34C759", ["KILL"] = "#FF3B30",
        ["RADIO"] = "#32ADE6", ["TC"] = "#FFD60A", ["MENU"] = "#FF9F0A", ["ESC"] = "#32ADE6",
    };

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
        // Posições medidas sobre wheel.png (1304×1304): centróide colorido de cada botão.
        Buttons = new List<WheelButtonViewModel>
        {
            new("N",     0.219, 0.334, "#BF5AF2"),
            new("PIT",   0.295, 0.347, "#FFD60A"),
            new("DRS",   0.716, 0.346, "#34C759"),
            new("KILL",  0.783, 0.335, "#FF3B30"),
            new("RADIO", 0.258, 0.462, "#32ADE6"),
            new("TC",    0.257, 0.520, "#FFD60A"),
            new("MENU",  0.746, 0.463, "#FF9F0A"),
            new("ESC",   0.746, 0.521, "#32ADE6"),
        };

        if (_session is not null)
        {
            _isConnected = _session.IsConnected;
            _session.StateReceived += OnState;
            _session.Connected += OnConnectionChanged;
            _session.Disconnected += OnConnectionChanged;
        }

        // Alterar a config das pás (função/modo/atuação/bite) marca "não salvo".
        BottomPair.PropertyChanged += OnConfigChanged;

        // Carrega o perfil salvo no arranque (config persiste entre execuções, como MOZA).
        // Fire-and-forget: LoadAsync tolera arquivo ausente (mantém os defaults acima).
        _ = LoadCommand.ExecuteAsync(null);
    }

    private void OnConfigChanged(object? sender, PropertyChangedEventArgs e)
    {
        MarkDirty();
        // Subconjunto que o firmware guarda → escreve na placa ao mudar (o resto é só app/JSON).
        switch (e.PropertyName)
        {
            case nameof(PaddlePairViewModel.Mode):
                WriteDeviceSetting(WheelSettingId.ClutchMode, BottomPair.Mode == PaddleMode.Independent ? 1 : 0);
                break;
            case nameof(PaddlePairViewModel.BitePoint):
                WriteDeviceSetting(WheelSettingId.ClutchBitePoint, BottomPair.BitePoint);
                break;
        }
    }

    /// <summary>Escreve na placa uma setting que o firmware persiste (modo/bite/brilho). Ignorado
    /// durante carga, no simulador ou desconectado.</summary>
    private void WriteDeviceSetting(WheelSettingId id, double value)
    {
        if (_loading || IsSimulator || _session is null || !_session.IsConnected)
            return;
        _ = _session.WriteSettingAsync(id, new SettingValue(SettingType.UInt8, value));
    }

    /// <summary>Marca a config como não salva (ignorado durante carga/aplicação de perfil).</summary>
    private void MarkDirty() { if (!_loading) IsDirty = true; }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (_session is null) return;
        await _session.ConnectAsync();
        System.Console.WriteLine($"[DriveLab] Volante ConnectAsync → IsConnected={_session.IsConnected}");
        await LoadFromDeviceAsync();   // (re)detectou o aro → reflete a config salva + o que a placa guarda
    }

    private bool CanConnect() => _session is not null && !IsConnected;

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task DisconnectAsync() => _session?.DisconnectAsync() ?? Task.CompletedTask;

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        // A carga da config ao (re)detectar acontece no ConnectCommand (LoadFromDeviceAsync),
        // que é o mesmo caminho do hotplug real (DeviceAutoConnector) e do simulador.
        IsConnected = _session?.IsConnected ?? false;
        if (!IsConnected)
        {
            // Desconectou: apaga reações (knobs/pressões) pra não ficar "travado" aceso.
            foreach (var k in Knobs) k.Glow = 0;
            foreach (var b in Buttons) b.IsPressed = false;
            foreach (var p in Paddles) p.IsPressed = false;
        }
    }

    /// <summary>Ao (re)detectar o aro: reflete a config salva na tela, descartando edições não salvas.
    /// Cores/função/contagem/atuação vêm do perfil do app (o firmware não as guarda); o subconjunto
    /// que a placa realmente persiste (modo/bite/brilho da embreagem) é lido do device — fonte da verdade.</summary>
    private async Task LoadFromDeviceAsync()
    {
        // 1) App-side: cores/função/contagem/atuação (perfil salvo em disco, junto do "Salvar no controlador").
        await LoadCommand.ExecuteAsync(null);

        // 2) Device é a fonte da verdade do que o firmware guarda (no simulador não há placa persistente).
        if (!IsSimulator && _session is not null && _session.IsConnected)
        {
            _loading = true;   // aplicar leitura da placa não conta como edição do usuário
            try
            {
                BottomPair.Mode = (await _session.ReadSettingAsync(WheelSettingId.ClutchMode)).AsDouble != 0
                    ? PaddleMode.Independent : PaddleMode.Combined;
                BottomPair.BitePoint = (int)(await _session.ReadSettingAsync(WheelSettingId.ClutchBitePoint)).AsDouble;
                LedBrightness = (byte)(await _session.ReadSettingAsync(WheelSettingId.LedBrightness)).AsDouble;
            }
            catch { /* leitura pode estourar timeout; o perfil salvo já foi aplicado acima */ }
            finally { _loading = false; }
        }

        IsDirty = false;   // acabou de (re)carregar do device: nada pendente
        PushLeds();
    }

    // Telemetria do aro → visual de pressão (mesmo caminho da simulação).
    // Mapa provisório: bits 0..7 = os 8 botões do desenho; bits 10/11 = marcha ↓/↑;
    // embreagens acendem pelo eixo (output alto). Ajustável quando o layout físico fechar.
    private void OnState(object? sender, WheelState s)
    {
        // Telemetria dirige botões/pás/knobs — no hardware real vem do firmware; no /simulator
        // vem do demo auto-aleatório do SimulatorWheelTransport.
        for (var i = 0; i < Buttons.Count && i < 32; i++)
            Buttons[i].IsPressed = s.IsButtonPressed(i);
        ShiftDown.IsPressed = s.IsButtonPressed(10);
        ShiftUp.IsPressed   = s.IsButtonPressed(11);

        // Embreagem: em modo COMBINADO as duas pás agem como uma — pressionar 1 acende as 2.
        var clutchL = s.ClutchLeft.Output  > 32768;
        var clutchR = s.ClutchRight.Output > 32768;
        if (BottomPair.Function == PaddleFunction.Clutch && BottomPair.Mode == PaddleMode.Combined)
        {
            var any = clutchL || clutchR;
            ClutchLeft.IsPressed = ClutchRight.IsPressed = any;
        }
        else
        {
            ClutchLeft.IsPressed  = clutchL;
            ClutchRight.IsPressed = clutchR;
        }

        // Knobs rotativos: girou (delta != 0) → acende; senão decai por frame.
        for (var i = 0; i < Knobs.Count; i++)
        {
            var d = i < s.EncoderDeltas.Length ? s.EncoderDeltas[i] : (sbyte)0;
            Knobs[i].Glow = d != 0 ? 1.0 : Knobs[i].Glow * 0.82;
        }

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

    partial void OnLedBrightnessChanged(byte value)
    {
        PushLeds();
        WriteDeviceSetting(WheelSettingId.LedBrightness, value);   // firmware persiste o brilho
        MarkDirty();
    }

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
            MarkDirty();
        }
    }

    partial void OnPaddleCountChanged(int value) => MarkDirty();

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

    /// <summary>Restaura cores dos botões e config das pás ao padrão de fábrica (marca não salvo).</summary>
    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void ResetDefaults()
    {
        _loading = true;   // aplicar o padrão não conta como edição individual
        try
        {
            foreach (var b in Buttons)
                if (DefaultColors.TryGetValue(b.Name, out var hex))
                    b.ColorHex = hex;
            PaddleCount = 4;
            var d = new PaddlePairViewModel();   // defaults canônicos das pás
            BottomPair.Function = d.Function;
            BottomPair.Mode = d.Mode;
            BottomPair.Actuation = d.Actuation;
            BottomPair.BitePoint = d.BitePoint;
            PushLeds();   // reflete no aro na hora
        }
        finally
        {
            _loading = false;
        }
        // Escreve na placa o subconjunto que o firmware guarda, senão o "Salvar no controlador"
        // persistiria os valores ANTIGOS da placa (as linhas acima rodaram sob _loading).
        WriteDeviceSetting(WheelSettingId.ClutchMode, BottomPair.Mode == PaddleMode.Independent ? 1 : 0);
        WriteDeviceSetting(WheelSettingId.ClutchBitePoint, BottomPair.BitePoint);
        IsDirty = true;   // padrão aplicado → precisa "Salvar no controlador"
    }

    /// <summary>Salva o perfil do app (cores/pás). Funciona offline; não mexe na flash do device.</summary>
    [RelayCommand]
    private Task Save() => _storage.SaveAsync(Export());

    /// <summary>Padrão dos outros módulos: só habilita quando há alteração e o aro está conectado.
    /// Persiste o perfil do app (cores/pás) e manda SaveToFlash p/ o device (calibração/brilho).</summary>
    [RelayCommand(CanExecute = nameof(CanSaveToController))]
    private async Task SaveToController()
    {
        await _storage.SaveAsync(Export());
        if (_session is not null && _session.IsConnected)
        {
            PushLeds();   // reenvia as cores atuais → o firmware as grava na flash junto do SaveToFlash
            await _session.SendCommandAsync(WheelCommandId.SaveToFlash);
        }
        IsDirty = false;
    }

    private bool CanSaveToController() => IsConnected && IsDirty;

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
        _loading = true;   // aplicar perfil não conta como "alteração do usuário"
        try
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
        finally
        {
            _loading = false;
            IsDirty = false;   // acabou de carregar: nada pendente
        }
    }

    public override void Dispose()
    {
        BottomPair.PropertyChanged -= OnConfigChanged;
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
