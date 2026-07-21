// ============================================================================
//  DriveLab
//  RevLightsViewModel.cs — VM do painel de rev-lights: liga/desliga a telemetria→LEDs e o modo de teste (simulado).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DriveLab.Core.Telemetry;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>
/// Painel de rev-lights: liga a telemetria de jogo à barra de LEDs do aro. "Ativar" arranca o laço do
/// <see cref="GameTelemetryService"/>; "Modo de teste" aponta uma <see cref="SimulatedTelemetrySource"/> para
/// validar de mesa (aro no USB, sem jogo/motor): o RPM sobe sozinho e a barra varre. O readout ao vivo
/// (fonte/RPM/marcha) vem do evento do serviço, marshalado para a thread de UI.
/// </summary>
public partial class RevLightsViewModel : ViewModelBase
{
    private readonly GameTelemetryService _service;
    private readonly IUiDispatcher _dispatcher;
    private readonly Func<double> _clock;
    private readonly Action _restoreButtons;

    public RevLightsViewModel(GameTelemetryService service, IUiDispatcher dispatcher, Func<double> clock, Action restoreButtons)
    {
        _service = service;
        _dispatcher = dispatcher;
        _clock = clock;
        _restoreButtons = restoreButtons;
        _service.TelemetryUpdated += OnTelemetry;
    }

    /// <summary>Liga/desliga a cadeia telemetria→LEDs.</summary>
    [ObservableProperty] private bool _enabled;

    /// <summary>Usa a fonte simulada (varredura de RPM) em vez das fontes reais — para validação de mesa.</summary>
    [ObservableProperty] private bool _testMode;

    [ObservableProperty] private string _activeSource = "—";
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private int _rpm;
    [ObservableProperty] private int _maxRpm;
    [ObservableProperty] private int _gear;

    partial void OnEnabledChanged(bool value)
    {
        if (value)
        {
            _service.Start();
        }
        else
        {
            _service.Stop();
            _restoreButtons();          // devolve as cores dos botões e apaga a barra
            ActiveSource = "—";
            HasData = false;
            Rpm = 0;
            Gear = 0;
        }
    }

    partial void OnTestModeChanged(bool value)
    {
        _service.ForcedSource = value ? new SimulatedTelemetrySource(_clock) : null;
    }

    private void OnTelemetry(object? sender, GameTelemetry t)
    {
        _dispatcher.Post(() =>
        {
            ActiveSource = _service.ActiveSourceName ?? "—";
            HasData = t.HasData;
            Rpm = (int)t.Rpm;
            MaxRpm = (int)t.MaxRpm;
            Gear = t.Gear;
        });
    }
}
