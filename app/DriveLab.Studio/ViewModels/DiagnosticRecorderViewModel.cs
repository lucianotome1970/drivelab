// ============================================================================
//  DriveLab
//  DiagnosticRecorderViewModel.cs — Grava a telemetria da base em CSV + marcações (loop de feedback FFB).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>Grava a telemetria ao vivo da base (ângulo/torque/corrente/tensão/temperaturas) num CSV,
/// mais as **marcações** do usuário ("aqui tremeu") na mesma trilha de tempo — o lado-app do loop de
/// feedback do FFB. O <see cref="TextWriter"/> vem de uma fábrica (arquivo em produção, StringWriter
/// em teste). Funciona já contra o simulador (que produz a mesma telemetria).</summary>
public sealed partial class DiagnosticRecorderViewModel : ViewModelBase
{
    private static readonly string[] Columns =
        { "angle_deg", "torque", "current_mA", "bus_mV", "fet_C", "motor_C" };

    private readonly BaseSession _session;
    private readonly Func<TextWriter> _writerFactory;
    private readonly Stopwatch _clock = new();
    private DiagnosticRecorder? _rec;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    [NotifyCanExecuteChangedFor(nameof(MarkCommand))]
    private bool _isRecording;

    [ObservableProperty] private string _markNote = "";
    [ObservableProperty] private int _rowCount;

    public DiagnosticRecorderViewModel(BaseSession session, Func<TextWriter>? writerFactory = null)
    {
        _session = session;
        _writerFactory = writerFactory ?? DefaultFileWriter;
        _session.StateReceived += OnState;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        _rec = new DiagnosticRecorder(_writerFactory(), Columns);
        RowCount = 0;
        _clock.Restart();
        IsRecording = true;
    }
    private bool CanStart() => !IsRecording;

    [RelayCommand(CanExecute = nameof(IsRecording))]
    private void Stop()
    {
        _clock.Stop();
        _rec?.Dispose();
        _rec = null;
        IsRecording = false;
    }

    /// <summary>Anota o instante atual ("aqui ficou notchy") na trilha de tempo do CSV.</summary>
    [RelayCommand(CanExecute = nameof(IsRecording))]
    private void Mark()
    {
        if (_rec is null)
            return;
        _rec.Mark(_clock.Elapsed.TotalMilliseconds, string.IsNullOrWhiteSpace(MarkNote) ? "mark" : MarkNote.Trim());
        RowCount = _rec.RowCount;
        MarkNote = "";
    }

    private void OnState(object? sender, BaseState s)
    {
        if (!IsRecording || _rec is null)
            return;
        _rec.Record(_clock.Elapsed.TotalMilliseconds, new double[]
        {
            s.AngleDeciDeg / 10.0, s.Torque, s.MotorCurrentMa, s.BusVoltageMv, s.FetTempC, s.MotorTempC,
        });
        RowCount = _rec.RowCount;
    }

    private static TextWriter DefaultFileWriter()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DriveLab", "diagnostics");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"diag-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        return new StreamWriter(path);
    }

    public override void Dispose()
    {
        _session.StateReceived -= OnState;
        _rec?.Dispose();
        base.Dispose();
    }
}
