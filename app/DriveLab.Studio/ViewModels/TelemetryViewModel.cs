// ============================================================================
//  DriveLab
//  TelemetryViewModel.cs — VM da tela de telemetria: séries de posição e torque para os gráficos.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.ObjectModel;
using DriveLab.Core.Protocol;
using DriveLab.Studio.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;

namespace DriveLab.Studio.ViewModels;

public sealed class TelemetryViewModel : ViewModelBase
{
    private const int MaxSamples = 240;
    private readonly BaseSession _session;

    public ObservableCollection<ObservableValue> PositionSamples { get; } = new();
    public ObservableCollection<ObservableValue> TorqueSamples { get; } = new();
    public ObservableCollection<ObservableValue> ClippingSamples { get; } = new();
    public ISeries[] Series { get; }

    /// <summary>Eixo Y com escala FIXA (±100%) — ver o porquê no construtor.</summary>
    public Axis[] YAxes { get; }

    /// <summary>Gravador de diagnóstico (CSV + marcações) — o lado-app do loop de feedback do FFB.</summary>
    public DiagnosticRecorderViewModel Recorder { get; }

    public TelemetryViewModel(BaseSession session)
    {
        _session = session;
        Recorder = new DiagnosticRecorderViewModel(session);
        Series = new ISeries[]
        {
            new LineSeries<ObservableValue> { Name = Localization.LocalizationManager.Get("Telemetry_Position"), Values = PositionSamples, GeometrySize = 0 },
            new LineSeries<ObservableValue> { Name = Localization.LocalizationManager.Get("Telemetry_Torque"), Values = TorqueSamples, GeometrySize = 0 },
            // CLIPPING junto das outras duas de propósito: ele só significa alguma coisa NO CONTEXTO
            // do que estava acontecendo. Clipping alto com torque no teto é a força saturando (baixe
            // o ganho); clipping alto com torque baixo é outra história. Em gráfico separado, essa
            // correlação se perde — que é justamente o que o usuário quer enxergar.
            new LineSeries<ObservableValue> { Name = Localization.LocalizationManager.Get("Telemetry_Clipping"), Values = ClippingSamples, GeometrySize = 0 },
        };

        // ESCALA FIXA em ±100%, a faixa real das duas grandezas (posição no curso e torque em % do
        // fundo de escala). Sem isto o gráfico auto-escala pelos dados: com a base parada e sem
        // jogo, as duas séries são praticamente constantes, e o LiveCharts amplia uma faixa de
        // fração de por cento até ocupar a tela — uma linha parada em 1,1% aparece colada no topo,
        // como se estivesse saturada. Relatado na bancada em 2026-08-10.
        //
        // Com o eixo fixo, parado = linha perto do zero (que é o que está acontecendo de verdade) e
        // o gráfico só se mexe quando há movimento ou força reais. Também deixa as duas séries
        // COMPARÁVEIS entre si e entre sessões — auto-escala muda o significado da altura a cada
        // instante, e isso é veneno num gráfico de diagnóstico.
        YAxes = new Axis[]
        {
            new Axis { MinLimit = -100, MaxLimit = 100, Name = "%" },
        };

        _session.StateReceived += OnState;
    }

    private void OnState(object? sender, BaseState state)
    {
        Append(PositionSamples, state.Position / 100.0);
        Append(TorqueSamples, state.Torque / 100.0);
        Append(ClippingSamples, state.ClippingPercent);   // já vem em 0..100
    }

    private static void Append(ObservableCollection<ObservableValue> series, double value)
    {
        series.Add(new ObservableValue(value));
        if (series.Count > MaxSamples)
            series.RemoveAt(0);
    }

    public override void Dispose()
    {
        _session.StateReceived -= OnState;
        Recorder.Dispose();
        base.Dispose();
    }
}
