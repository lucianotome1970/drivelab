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
    private readonly DeviceSession _session;

    public ObservableCollection<ObservableValue> PositionSamples { get; } = new();
    public ObservableCollection<ObservableValue> TorqueSamples { get; } = new();
    public ISeries[] Series { get; }

    public TelemetryViewModel(DeviceSession session)
    {
        _session = session;
        Series = new ISeries[]
        {
            new LineSeries<ObservableValue> { Name = "Posição %", Values = PositionSamples, GeometrySize = 0 },
            new LineSeries<ObservableValue> { Name = "Torque %", Values = TorqueSamples, GeometrySize = 0 },
        };
        _session.StateReceived += OnState;
    }

    private void OnState(object? sender, DeviceState state)
    {
        Append(PositionSamples, state.Position / 100.0);
        Append(TorqueSamples, state.Torque / 100.0);
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
        base.Dispose();
    }
}
