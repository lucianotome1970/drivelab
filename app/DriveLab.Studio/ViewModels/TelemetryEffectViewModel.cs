// ============================================================================
//  DriveLab
//  TelemetryEffectViewModel.cs — Linha de UI de um efeito por telemetria: liga/desliga + ganho.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DriveLab.Core.Telemetry.Effects;

namespace DriveLab.Studio.ViewModels;

/// <summary>Um efeito por telemetria na UI (RPM/Marcha/ABS/Slip): reflete e edita o <see cref="ITelemetryEffect"/>
/// subjacente. Ganho em 0..200% (permite reforçar) mapeado para o 0..2 do efeito.</summary>
public partial class TelemetryEffectViewModel : ViewModelBase
{
    private readonly ITelemetryEffect _effect;

    public TelemetryEffectViewModel(ITelemetryEffect effect)
    {
        _effect = effect;
        _enabled = effect.Enabled;
        _gainPercent = (int)Math.Round(effect.Gain * 100);
    }

    public string Name => _effect.Name;

    [ObservableProperty] private bool _enabled;
    [ObservableProperty] private int _gainPercent;

    partial void OnEnabledChanged(bool value) => _effect.Enabled = value;
    partial void OnGainPercentChanged(int value) => _effect.Gain = value / 100f;
}
