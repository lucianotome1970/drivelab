// ============================================================================
//  DriveLab
//  BaseViewModel.cs — VM do card da base no dashboard: controle único de força total (TotalStrength).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.ViewModels;

/// <summary>Card da base no dashboard: um único controle de força total
/// (<see cref="BaseSettingId.TotalStrength"/>). Envolve o MESMO <see cref="BaseSession"/> do
/// volante — lê o valor ao conectar e grava ao alterar (espelha o padrão do
/// <see cref="DashboardViewModel"/> p/ MotionRange). Não descarta a sessão (compartilhada).</summary>
public partial class BaseViewModel : ViewModelBase, IPendingWrite
{
    private readonly BaseSession _session;
    private bool _loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotorArmado))]
    [NotifyPropertyChangedFor(nameof(MotorDesarmado))]
    [NotifyPropertyChangedFor(nameof(PodeRearmar))]
    [NotifyPropertyChangedFor(nameof(EstadoTooltip))]
    private bool _isConnected;

    [ObservableProperty]
    private int _totalStrength = 100;

    // ── ESTADO DO MOTOR, no cartão da base ──────────────────────────────────────────────────────
    // "O motor está ligado?" decide se é seguro encostar no volante. Fica no cartão da base porque
    // é ali que se olha para saber como a base está — e não numa aba que pode não estar aberta.
    //
    // ⚠️ TRÊS estados, não dois: armada (verde), conectada e desarmada (vermelho), sem base
    // (cinza). "Desarmada" e "não tem base" são situações diferentes — uma é um volante parado que
    // pode ganhar força a qualquer momento, a outra é um app sem hardware. Pintar as duas de
    // vermelho ensinaria a ignorar o vermelho.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotorArmado))]
    [NotifyPropertyChangedFor(nameof(MotorDesarmado))]
    [NotifyPropertyChangedFor(nameof(PodeRearmar))]
    [NotifyPropertyChangedFor(nameof(EstadoTooltip))]
    private bool _forceEnabled;

    // ⚠️ TRÊS ESTADOS + "NÃO SEI", e o último não é preciosismo.
    //
    // O estado do motor vem da TELEMETRIA. Se ela para de chegar — base travada, cabo, ou o
    // experimento que a desliga —, o campo ForceEnabled fica no último valor recebido ou em false, e
    // a tela passa a AFIRMAR "desarmada" sobre uma base que pode estar armada e aplicando força. É a
    // informação errada exatamente onde ela importa para a segurança.
    //
    // Descoberto em 16/08/2026, com a telemetria desligada de propósito: a base estava armada, em
    // malha fechada e sem erro nenhum, e o app dizia que estava desarmada. O resto da tela já trata
    // isso direito — os campos mostram "—" quando não sabem; a bolinha é que afirmava.
    //
    // Agora ela só afirma com telemetria RECENTE. Sem ela, mostra o mesmo cinza de "sem base", que é
    // honesto: em ambos os casos o app não tem como saber o que a base está fazendo.
    public bool MotorArmado    => IsConnected && TelemetriaRecente && ForceEnabled;
    public bool MotorDesarmado => IsConnected && TelemetriaRecente && !ForceEnabled;

    /// <summary>Chegou telemetria nos últimos segundos. Três segundos é folgado: ela chega a cada
    /// 40 ms, então setenta e cinco pacotes perdidos seguidos já valem como silêncio.</summary>
    public bool TelemetriaRecente =>
        _ultimaTelemetria is { } t && (DateTime.UtcNow - t) < TimeSpan.FromSeconds(3);

    private DateTime? _ultimaTelemetria;

    /// <summary>A base está conectada e o motor NÃO está armado — ou seja, algo o parou e ele não
    /// volta sozinho. É o único estado em que oferecer "armar" faz sentido.
    ///
    /// <para>⚠️ ISTO PRECISA VIVER NO PAINEL, e não só na aba Hardware. O campo "Permitir armar o
    /// motor" fica na aba de hardware, que só aparece no modo criador — quando a proteção retira a
    /// permissão, o usuário comum não tem NADA para ligar e a base fica desarmada para sempre. Foi
    /// exatamente o buraco que a bancada apontou em 15/08/2026, depois de eu ter removido este botão
    /// achando que o campo bastava.</para></summary>
    // Sem saber o estado, não oferecemos "armar": o botão apareceria numa base que já está armada, e
    // apertá-lo mandaria um comando desnecessário para quem está no meio de uma volta.
    public bool PodeRearmar => IsConnected && TelemetriaRecente && !ForceEnabled;

    public string EstadoTooltip =>
        !IsConnected            ? L.Get("BaseState_Offline")
        : !TelemetriaRecente    ? L.Get("BaseState_Unknown")
        : ForceEnabled ? L.Get("BaseState_Armed")
                       : L.Get("BaseState_Disarmed");

    public BaseViewModel(BaseSession session)
    {
        _session = session;
        _session.Connected += OnConnected;
        _session.Disconnected += OnDisconnected;
        _session.SettingChanged += OnSettingChanged;
        // O estado do motor só existe na telemetria — nenhum evento de conexão o informa.
        _session.StateReceived += (_, estado) =>
        {
            _ultimaTelemetria = DateTime.UtcNow;
            ForceEnabled = estado.Flags.HasFlag(BaseFlags.ForceEnabled);
            // Mesmo que o valor não mude, o RELÓGIO mudou: sem avisar, a bolinha ficaria cinza para
            // sempre numa base parada e armada, que não gera mudança de flag nenhuma.
            OnPropertyChanged(nameof(TelemetriaRecente));
            OnPropertyChanged(nameof(MotorArmado));
            OnPropertyChanged(nameof(MotorDesarmado));
            OnPropertyChanged(nameof(PodeRearmar));
            OnPropertyChanged(nameof(EstadoTooltip));
        };
        IsConnected = _session.IsConnected;
        // Já conectado ao criar o card → o Connected não dispara mais; lê a força da placa agora.
        if (IsConnected)
            OnConnected(this, EventArgs.Empty);
    }

    private async void OnConnected(object? sender, EventArgs e)
    {
        IsConnected = true;
        try
        {
            // Mostra o valor real do dispositivo (não o default do VM).
            _loading = true;
            var value = await _session.ReadSettingAsync(BaseSettingId.TotalStrength);
            TotalStrength = (int)value.AsDouble;
        }
        catch
        {
            // Leitura pode falhar/expirar (ex.: dispositivo sumiu); não derruba o app.
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>Devolve a permissão e rearma. ESCREVE o campo em vez de mandar só o comando: é a
    /// permissão que a proteção retirou, então é ela que precisa voltar — e o firmware trata o
    /// religar como rearme (destrava a guarda e limpa os erros).
    ///
    /// <para>Escrever o setting mantém as duas telas contando a mesma história: quem tem a aba
    /// Hardware vê o campo voltar a 1, e quem não tem usa este botão. Um comando solto rearmaria com
    /// o campo em 0, e a aba Hardware passaria a mentir.</para></summary>
    [RelayCommand]
    private async Task RearmarAsync()
    {
        if (!_session.IsConnected) return;
        await _session.WriteSettingAsync(BaseSettingId.MotorEnable, new SettingValue(SettingType.UInt8, 1));
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        IsConnected = false;
        // Sem isto a bolinha ficaria verde depois que a base sumisse: telemetria que parou de
        // chegar não gera evento nenhum, e o último valor recebido continuaria valendo.
        ForceEnabled = false;
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Id != BaseSettingId.TotalStrength)
            return;
        _loading = true;
        TotalStrength = (int)e.Value.AsDouble;
        _loading = false;
    }

    /// <summary>Alterado na tela e ainda NÃO enviado à base.</summary>
    public bool IsModified { get; private set; }

    /// <summary>O usuário mexeu na força (não dispara em load/eco do device).</summary>
    public event EventHandler? Edited;

    /// <summary>Envia a força para a base. Chamado SÓ pelo "Salvar no controlador" do dashboard.</summary>
    public async Task PushAsync()
    {
        if (!_session.IsConnected)
            return;
        var d = BaseSettingsSchema.Get(BaseSettingId.TotalStrength);
        var clamped = (int)Math.Clamp(TotalStrength, d.Min, d.Max);
        await _session.WriteSettingAsync(BaseSettingId.TotalStrength, new SettingValue(d.Type, clamped));
        IsModified = false;   // já está na base
    }

    partial void OnTotalStrengthChanged(int value)
    {
        // NÃO envia para a base aqui. Arrastar o controle reconfigurava a base ao vivo, com o motor
        // possivelmente ARMADO, e a cada pixel do slider. Quem envia é o "Salvar no controlador".
        if (_loading || !_session.IsConnected)
            return;
        IsModified = true;
        Edited?.Invoke(this, EventArgs.Empty);
    }

    public override void Dispose()
    {
        _session.Connected -= OnConnected;
        _session.Disconnected -= OnDisconnected;
        _session.SettingChanged -= OnSettingChanged;
        base.Dispose();
    }
}
