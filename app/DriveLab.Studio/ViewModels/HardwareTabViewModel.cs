// ============================================================================
//  DriveLab
//  HardwareTabViewModel.cs — VM da aba Hardware (só no modo criador): monitor de telemetria + settings de
//  hardware. O criador configura os campos e SALVA na base (a base é a fonte de verdade; sem JSON).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Aba "Hardware" (só aparece no modo avançado/criador): monitor de telemetria + os settings de
/// hardware. O criador edita os campos e usa o Save da página (grava na base — a fonte de verdade). É uma
/// <see cref="SettingsGroupViewModel"/> para que Save/Reset da página ajam sobre os campos desta aba.</summary>
public sealed partial class HardwareTabViewModel : SettingsGroupViewModel
{
    public HardwareMonitorViewModel Monitor { get; }

    // Cogging: presença da tabela na flash (via flag de telemetria) + status do botão de calibrar.
    [ObservableProperty] private bool _coggingPresent;
    [ObservableProperty] private string? _coggingStatus;

    private readonly BaseSession _session;

    public HardwareTabViewModel(BaseSession session, string title, IEnumerable<BaseSettingId> ids)
        : base(session, title, ids)
    {
        _session = session;
        Monitor = new HardwareMonitorViewModel(session);
        _session.StateReceived += OnState;

        // Mantém o Kt do monitor sincronizado com o campo torque_constant desta aba (valor inicial + edições/loads),
        // p/ o monitor estimar torque = Kt·corrente. Só o criador vê/edita (aba Hardware = modo avançado).
        _ktField = Fields.FirstOrDefault(f => f.SettingId == BaseSettingId.TorqueConstant);
        if (_ktField is not null)
        {
            Monitor.TorqueConstant = _ktField.Value;
            _ktField.PropertyChanged += OnKtFieldChanged;
        }

        // Encadeia os três campos do encoder: MODELO manda na TECNOLOGIA (só aparecem as que aquele
        // sensor oferece), e a tecnologia manda na RESOLUÇÃO (rótulo e conversão: PPR em ABZ,
        // contagens no resto). Sem esse encadeamento a tela deixaria escolher combinação inexistente.
        _modelField = Fields.FirstOrDefault(f => f.SettingId == BaseSettingId.EncoderType);
        _techField  = Fields.FirstOrDefault(f => f.SettingId == BaseSettingId.EncoderInterface);
        _cprField   = Fields.FirstOrDefault(f => f.SettingId == BaseSettingId.EncoderCpr);

        if (_modelField is not null && _techField is not null)
        {
            _techField.RefreshOptions((int)_modelField.Value);
            _modelField.PropertyChanged += OnEncoderModelChanged;
        }
        if (_techField is not null)
        {
            ApplyTech();
            _techField.PropertyChanged += OnEncoderTechChanged;
        }
    }

    private readonly SettingFieldViewModel? _ktField;
    private readonly SettingFieldViewModel? _modelField;
    private readonly SettingFieldViewModel? _techField;
    private readonly SettingFieldViewModel? _cprField;

    private void OnEncoderModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingFieldViewModel.Value)) return;
        if (_modelField is null || _techField is null) return;
        _techField.RefreshOptions((int)_modelField.Value);
        ApplyTech();
    }

    private void OnEncoderTechChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingFieldViewModel.Value)) ApplyTech();
    }

    /// <summary>Aplica a tecnologia ao campo de resolução e, se o catálogo souber o valor de fábrica
    /// daquele par sensor+tecnologia, já preenche — é o que faz o CPR sair certo sem ninguém calcular.</summary>
    private void ApplyTech()
    {
        if (_techField is null || _cprField is null) return;

        var tech  = (EncoderTech)(int)System.Math.Round(_techField.Value);
        var model = _modelField is null ? EncoderCatalog.Generico : (int)System.Math.Round(_modelField.Value);
        _cprField.ApplyEncoderTech(tech, model);
        var padrao = EncoderCatalog.DefaultResolution(model, tech);
        if (padrao > 0) _cprField.Value = padrao;
    }

    private void OnKtFieldChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingFieldViewModel.Value) && _ktField is not null)
            Monitor.TorqueConstant = _ktField.Value;
    }

    /// <summary>"Tabela presente na flash" vs "sem tabela" — texto derivado da flag de telemetria.</summary>
    public string CoggingPresentText => CoggingPresent
        ? "Tabela de cogging presente na flash (compensação ativa)."
        : "Sem tabela de cogging na flash (motor sem compensação).";

    private void OnState(object? sender, BaseState s)
    {
        CoggingPresent = s.Flags.HasFlag(BaseFlags.CoggingLoaded);
    }

    partial void OnCoggingPresentChanged(bool value) => OnPropertyChanged(nameof(CoggingPresentText));

    /// <summary>Dispara a calibração de cogging no dispositivo. A rotina de coleta EXIGE motor energizado —
    /// só roda na bancada (Stage 1); com o motor desligado o firmware só registra o pedido. Só o criador
    /// tem acesso (esta aba só aparece no modo avançado).</summary>
    [RelayCommand]
    private async Task CalibrateCoggingAsync()
    {
        try
        {
            await _session.SendCommandAsync(BaseCommand.CalibrateCogging);
            CoggingStatus = "Pedido enviado. A calibração roda na bancada com o motor energizado (Stage 1); " +
                            "a tabela é gravada na flash ao terminar.";
        }
        catch (Exception ex)
        {
            CoggingStatus = $"Falha ao enviar o comando: {ex.Message}";
        }
    }

    public override void Dispose()
    {
        _session.StateReceived -= OnState;
        if (_ktField is not null) _ktField.PropertyChanged -= OnKtFieldChanged;
        Monitor.Dispose();
        base.Dispose();
    }
}
