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

    public bool MotorArmado    => IsConnected && ForceEnabled;
    public bool MotorDesarmado => IsConnected && !ForceEnabled;

    /// <summary>A permissão está ligada e o motor NÃO está armado — ou seja, algo o desarmou e ele
    /// não volta sozinho. É o único estado em que oferecer "Armar agora" faz sentido.
    ///
    /// <para>Sem isto, quem chegava aqui via o botão de permissão já ligado e não tinha o que ligar:
    /// a tela oferecia a configuração certa e nenhuma ação. Ver o commit do rótulo "Permitir armar".</para></summary>
    public bool PodeRearmar => IsConnected && !ForceEnabled && PermissaoLigada;

    /// <summary>Cópia local do setting de permissão (BaseSettingId.MotorEnable), lida da base. Sem
    /// ela não dá para distinguir "desarmado porque você desligou" de "desarmado apesar de ligado" —
    /// e só o segundo caso pede o botão.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeRearmar))]
    private bool _permissaoLigada;

    public string EstadoTooltip =>
        !IsConnected  ? L.Get("BaseState_Offline")
        : ForceEnabled ? L.Get("BaseState_Armed")
                       : L.Get("BaseState_Disarmed");

    public BaseViewModel(BaseSession session)
    {
        _session = session;
        _session.Connected += OnConnected;
        _session.Disconnected += OnDisconnected;
        _session.SettingChanged += OnSettingChanged;
        // O estado do motor só existe na telemetria — nenhum evento de conexão o informa.
        _session.StateReceived += (_, estado) => ForceEnabled = estado.Flags.HasFlag(BaseFlags.ForceEnabled);
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
            var perm = await _session.ReadSettingAsync(BaseSettingId.MotorEnable);
            PermissaoLigada = perm.AsDouble != 0;
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

    /// <summary>Rearma por pedido explícito de quem usa. Só existe caminho para cá quando a
    /// permissão está ligada e o motor está desarmado — ver PodeRearmar.
    ///
    /// <para>⚠️ Isto passa por cima de uma proteção que decidiu travar. A tela avisa o que aconteceu
    /// ANTES de oferecer o botão; aqui só enviamos.</para></summary>
    [RelayCommand]
    private Task RearmarAsync() =>
        _session.IsConnected ? _session.SendCommandAsync(BaseCommand.Rearm) : Task.CompletedTask;

    private void OnDisconnected(object? sender, EventArgs e)
    {
        IsConnected = false;
        // Sem isto a bolinha ficaria verde depois que a base sumisse: telemetria que parou de
        // chegar não gera evento nenhum, e o último valor recebido continuaria valendo.
        ForceEnabled = false;
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        // A permissão pode ser mexida na aba Hardware; sem acompanhar aqui, o botão de rearmar
        // ficaria decidindo por um valor lido uma vez, na conexão.
        if (e.Id == BaseSettingId.MotorEnable) { PermissaoLigada = e.Value.AsDouble != 0; return; }
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
