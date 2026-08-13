// ============================================================================
//  DriveLab
//  TelemetryEffectsViewModel.cs — Painel dos efeitos de FFB por telemetria: master + por-efeito + readout.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using DriveLab.Core.Telemetry;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>
/// Painel dos efeitos por telemetria (ABS/slip/marcha/RPM) enviados à base. Controla o master (liga/desliga +
/// ganho geral) e cada efeito (toggle + ganho), tudo escrevendo direto no <see cref="GameTelemetryService"/> e
/// no seu mixer. O readout mostra a força de efeitos que está sendo enviada, vinda do evento do serviço.
/// </summary>
public partial class TelemetryEffectsViewModel : ViewModelBase
{
    private readonly GameTelemetryService _service;
    private readonly IUiDispatcher _dispatcher;

    public TelemetryEffectsViewModel(GameTelemetryService service, IUiDispatcher dispatcher)
    {
        _service = service;
        _dispatcher = dispatcher;
        _enabled = service.EffectsEnabled;
        _masterGainPercent = (int)Math.Round((service.EffectMixer?.MasterGain ?? 1f) * 100);
        Effects = service.EffectMixer?.Effects.Select(e => new TelemetryEffectViewModel(e)).ToList()
                  ?? new List<TelemetryEffectViewModel>();
        _service.TelemetryUpdated += OnTelemetry;
    }

    /// <summary>Um item por efeito do mixer (RPM/Marcha/ABS/Slip).</summary>
    public IReadOnlyList<TelemetryEffectViewModel> Effects { get; }

    /// <summary>Liga/desliga o envio dos efeitos à base.</summary>
    [ObservableProperty] private bool _enabled;

    /// <summary>Ganho mestre (0..100%).</summary>
    [ObservableProperty] private int _masterGainPercent;

    /// <summary>Força de efeitos sendo enviada, em 0..100% (módulo). Só readout.</summary>
    [ObservableProperty] private int _forcePercent;

    partial void OnEnabledChanged(bool value) => _service.EffectsEnabled = value;

    partial void OnMasterGainPercentChanged(int value)
    {
        if (_service.EffectMixer is not null)
            _service.EffectMixer.MasterGain = value / 100f;
    }

    private void OnTelemetry(object? sender, GameTelemetry t)
    {
        _dispatcher.Post(() =>
            ForcePercent = (int)Math.Round(Math.Abs(_service.LastEffectForce) / 255.0 * 100));
    }
}
