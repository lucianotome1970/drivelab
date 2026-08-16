// ============================================================================
//  DriveLab
//  DashboardViewModel.cs — VM do dashboard do volante: centralizar, ajustar ângulo máximo e status de conexão.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Controle cujo valor fica só na tela até o "Salvar no controlador" — a base é a fonte de
/// verdade e nada vai para ela por movimento de slider. Mesmo contrato do SettingFieldViewModel.</summary>
public interface IPendingWrite
{
    bool IsModified { get; }
    Task PushAsync();
    event EventHandler? Edited;
}

public partial class DashboardViewModel : ViewModelBase
{
    private readonly BaseSession _session;
    private readonly WheelDeviceSession? _rim;
    private readonly CenterHotkeyController? _centerHotkey;

    /// <summary>Controles de OUTROS cards (ex.: força total, no BaseViewModel) que este Salvar envia
    /// junto — o botão vive aqui, mas a regra vale para a tela toda.</summary>
    private readonly List<IPendingWrite> _pending = new();

    /// <summary>O ângulo escolhido ainda não foi enviado à base.</summary>
    private bool _motionRangePending;

    /// <summary>Liga um controle de outro card a este Salvar (o CompositionRoot faz a amarração).</summary>
    public void AttachPending(IPendingWrite p)
    {
        _pending.Add(p);
        p.Edited += (_, _) => IsDirty = true;
    }

    /// <summary>true enquanto o usuário está atribuindo o atalho: o próximo gesto (HID/teclado) é capturado.</summary>
    [ObservableProperty] private bool _isAssigningCenterButton;

    /// <summary>Mapeamentos ativos do atalho (estilo ACC: vários ao mesmo tempo, cada um com seu ×).</summary>
    public ObservableCollection<CenterBindingRowViewModel> CenterBindings { get; } = new();

    /// <summary>true quando não há nenhum mapeamento (a UI mostra um "—" / dica).</summary>
    public bool HasNoCenterBindings => CenterBindings.Count == 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CenterCommand))]
    [NotifyCanExecuteChangedFor(nameof(SetMaxAngleCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveToControllerCommand))]
    private bool _isConnected;

    /// <summary>Há mudança feita no dashboard que ainda não foi gravada na flash da base (mesmo padrão
    /// do HandbrakeViewModel). O botão "Salvar no controlador" só acende com isto — assim o usuário vê
    /// que falta gravar, e não fica clicando à toa quando app e flash já estão iguais.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveToControllerCommand))]
    private bool _isDirty;

    /// <summary>Conexão do ARO removível (rim). O card "Volante" do dash acende por esta —
    /// é o dispositivo que o usuário pluga. O ângulo/Center vêm da base (<see cref="IsConnected"/>).</summary>
    [ObservableProperty]
    private bool _isWheelConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AngleText))]
    private double _angleDegrees;

    /// <summary>Ângulo já formatado para a tela.
    ///
    /// Existe por causa do "-0°": o volante a -0,4° arredonda para -0,0 em ponto flutuante, e o
    /// formato "0" imprime isso como "-0" — que não é um ângulo que exista. Aparecia sempre que a
    /// base parava um triz à esquerda do centro. Arredondar não resolve sozinho; é preciso
    /// normalizar o zero negativo, que em IEEE-754 é um valor distinto do zero positivo.</summary>
    public string AngleText
    {
        get
        {
            var g = Math.Round(AngleDegrees);
            if (g == 0) g = 0;   // -0,0 == 0,0 é true, mas a atribuição troca o SINAL do zero
            return g.ToString("0", CultureInfo.InvariantCulture) + "°";
        }
    }

    [ObservableProperty]
    private double _positionPercent;

    [ObservableProperty]
    private int _motionRange = 900;

    public DashboardViewModel(BaseSession session, WheelDeviceSession? rim = null,
                              CenterHotkeyController? centerHotkey = null)
    {
        _session = session;
        _rim = rim;
        _centerHotkey = centerHotkey;
        _session.StateReceived += OnState;
        _session.WheelAngleReceived += OnWheelAngle;
        _session.Connected += OnConnected;
        _session.Disconnected += OnDisconnected;
        _session.SettingChanged += OnSettingChanged;
        IsConnected = _session.IsConnected;
        // Já conectado ao criar a tela → o evento Connected não vem mais; lê agora (ver
        // SettingsGroupViewModel: mesma armadilha, o ângulo ficava no default em vez do da placa).
        if (IsConnected)
            OnConnected(this, EventArgs.Empty);

        if (_rim is not null)
        {
            _rim.Connected += OnRimChanged;
            _rim.Disconnected += OnRimChanged;
            _isWheelConnected = _rim.IsConnected;
        }

        // Atalho de centralizar (HID/teclado): o controlador junta as fontes, aprende os gestos e dispara
        // o ResetCenter. A VM reflete a LISTA de mapeamentos e o estado de captura na UI.
        if (_centerHotkey is not null)
        {
            _centerHotkey.Changed += OnCenterHotkeyChanged;
            IsAssigningCenterButton = _centerHotkey.IsAssigning;
            RebuildCenterBindings();
        }
    }

    private void OnRimChanged(object? sender, EventArgs e) => IsWheelConnected = _rim?.IsConnected ?? false;

    private void OnCenterHotkeyChanged(object? sender, EventArgs e)
    {
        IsAssigningCenterButton = _centerHotkey!.IsAssigning;
        RebuildCenterBindings();
    }

    private void RebuildCenterBindings()
    {
        CenterBindings.Clear();
        if (_centerHotkey is not null)
            foreach (var b in _centerHotkey.Bindings)
                CenterBindings.Add(new CenterBindingRowViewModel(b, () => _centerHotkey.Remove(b)));
        OnPropertyChanged(nameof(HasNoCenterBindings));
    }

    /// <summary>Inicia/cancela a captura de um NOVO atalho (o próximo gesto vira mais um mapeamento).</summary>
    [RelayCommand]
    private void ToggleAssignCenterButton() => _centerHotkey?.ToggleAssign();

    /// <summary>Remove TODOS os mapeamentos do atalho de centralizar.</summary>
    [RelayCommand]
    private void ClearAllCenterButtons() => _centerHotkey?.ClearAll();

    /// <summary>Persiste na flash da base o que está no dashboard (ângulo de giro / centro).
    /// O botão "Salvar no controlador" das abas de config não alcança o dashboard: o
    /// <c>MotionRange</c> vive AQUI, então sem isto o ângulo escolhido se perde no power-cycle
    /// (constatado na bancada em 2026-08-05). Mesmo comando do firmware (<c>CMD_SAVE</c>): grava o
    /// blob de settings na FFB_NVM com o motor em IDLE — a força cai ~1 s e o motor re-arma.</summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveToControllerAsync()
    {
        // ENVIA primeiro, persiste depois: mexer nos controles não manda mais nada para a base
        // (nem o ângulo, nem a força total). Só aqui a placa é reconfigurada, de uma vez.
        if (_motionRangePending)
        {
            await _session.WriteSettingAsync(BaseSettingId.MotionRange,
                                             new SettingValue(SettingType.UInt16, MotionRange));
            _motionRangePending = false;
        }
        foreach (var p in _pending)
            if (p.IsModified)
                await p.PushAsync();

        await _session.SendCommandAsync(BaseCommand.SaveSettings);
        IsDirty = false;   // gravou na flash: app == firmware
    }

    private bool CanSave() => IsConnected && IsDirty;

    public override void Dispose()
    {
        _session.StateReceived -= OnState;
        _session.WheelAngleReceived -= OnWheelAngle;
        _session.Connected -= OnConnected;
        _session.Disconnected -= OnDisconnected;
        _session.SettingChanged -= OnSettingChanged;
        if (_rim is not null)
        {
            _rim.Connected -= OnRimChanged;
            _rim.Disconnected -= OnRimChanged;
        }
        if (_centerHotkey is not null)
        {
            _centerHotkey.Changed -= OnCenterHotkeyChanged;
            _centerHotkey.Dispose();   // solta o hook global de teclado e os handles HID
        }
        base.Dispose();
    }

    private async void OnConnected(object? sender, EventArgs e)
    {
        IsConnected = true;
        try
        {
            // Mostra o valor real do dispositivo (não o default do VM).
            var value = await _session.ReadSettingAsync(BaseSettingId.MotionRange);
            MotionRange = (int)value.AsDouble;
            IsDirty = false;   // acabou de ler da base: app == flash
        }
        catch
        {
            // Leitura pode falhar/expirar (ex.: dispositivo sumiu); não derruba o app.
        }
    }

    private void OnDisconnected(object? sender, EventArgs e) => IsConnected = false;

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Id == BaseSettingId.MotionRange)
            MotionRange = (int)e.Value.AsDouble;
    }

    /// <summary>Ângulo vindo do relatório do JOGO, a 1 kHz. É esta a fonte do desenho.
    ///
    /// <para>Antes o desenho seguia a telemetria, a 25 Hz, e precisava de interpolação para não ficar
    /// aos saltos — com toda a complicação de detectar se a animação está viva. A mesma informação já
    /// passava pelo app quarenta vezes mais rápido, no relatório que a base manda para o jogo, e era
    /// descartada.</para>
    ///
    /// <para>⚠️ SATURA EM ±540°, que é o alcance do eixo. Com o curso em 900° sobra folga; acima de
    /// 1080° no total o desenho para nos extremos enquanto o volante real continua. Quem precisar de
    /// curso maior volta a depender da telemetria para os extremos.</para></summary>
    private void OnWheelAngle(object? sender, double graus)
    {
        _ultimoAnguloDoJogo = DateTime.UtcNow;
        _targetAngle = graus;
        AngleDegrees = graus;   // 1 kHz não precisa de interpolação: já chega mais rápido que a tela
        _hasAngle = true;
    }

    /// <summary>Quando chegou o último ângulo pelo relatório do jogo. Null = nunca chegou.</summary>
    private DateTime? _ultimoAnguloDoJogo;

    /// <summary>A fonte rápida está viva? Meio segundo de silêncio já é muito para algo que chega
    /// mil vezes por segundo — e é curto o bastante para a reserva assumir sem ninguém notar.</summary>
    private bool AnguloDoJogoVivo =>
        _ultimoAnguloDoJogo is { } t && (DateTime.UtcNow - t) < TimeSpan.FromSeconds(0.5);

    private void OnState(object? sender, BaseState state)
    {
        // ⚠️ A TELEMETRIA NÃO MEXE MAIS NO ÂNGULO. Ele vem do relatório que a base manda para o JOGO,
        // a 1 kHz — quarenta vezes mais rápido e pelo caminho que não trava.
        //
        // Isso libera a telemetria a ser LENTA, que é o ganho de verdade. Os 40 ms dela existiam por
        // causa deste desenho: o firmware registra que com 100 ms o ângulo pulava até 100° entre
        // amostras e a tela saltava. Sem essa obrigação, ela cai para uma taxa de painel — temperatura
        // e corrente não mudam mil vezes por segundo — e a pressão sobre o endpoint cai junto.
        PositionPercent = state.Position / 100.0;
        IsConnected = _session.IsConnected;
    }

    /// <summary>Ângulo do volante, em graus, vindo do relatório do jogo.</summary>
    private double _targetAngle;
    private bool _hasAngle;

    /// <summary>Existia para a view avançar a interpolação a cada quadro. Não interpolamos mais — a
    /// 1 kHz o valor já chega mais rápido do que a tela desenha —, mas a view continua chamando, e
    /// tirar o método de lá é mudança em outra camada. Fica sem efeito e documentado, em vez de
    /// deixar a view chamando algo que sumiu.</summary>
    public void TickAngleAnimation(double dtSeconds) { _ = dtSeconds; }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task CenterAsync()
    {
        if (!_session.IsConnected)
            return Task.CompletedTask;
        return _session.SendCommandAsync(BaseCommand.ResetCenter);
    }

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private void SetMaxAngle(string degrees)
    {
        if (!_session.IsConnected)
            return;
        // NÃO envia para a base aqui — o ângulo fica só na tela até o "Salvar no controlador".
        MotionRange = int.Parse(degrees, CultureInfo.InvariantCulture);
        _motionRangePending = true;
        IsDirty = true;
    }
}
