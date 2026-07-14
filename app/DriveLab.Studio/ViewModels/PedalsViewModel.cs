using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.ViewModels;

public sealed partial class PedalsViewModel : ViewModelBase
{
    private const int MaxSamples = 240;

    private readonly PedalDeviceSession _session;
    private readonly IPedalProfileStorage _storage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveToControllerCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    private bool _isConnected;

    public IReadOnlyList<PedalColumnViewModel> Columns { get; }

    /// <summary>Rótulo da fonte ativa (ex.: "Simulador" / "Simagic P2000 — leitura").</summary>
    public string SourceLabel => _session.SourceLabel;

    /// <summary>Falso para fonte read-only (Simagic): "Salvar no controlador" fica desabilitado.</summary>
    public bool CanSaveToController => _session.SupportsConfig;

    public ObservableCollection<ObservableValue> ClutchSamples { get; } = new();
    public ObservableCollection<ObservableValue> BrakeSamples { get; } = new();
    public ObservableCollection<ObservableValue> ThrottleSamples { get; } = new();
    public ISeries[] CombinedSeries { get; }

    public PedalsViewModel(PedalDeviceSession session, IPedalProfileStorage storage)
    {
        _session = session;
        _storage = storage;

        Columns = new List<PedalColumnViewModel>
        {
            new(session, PedalIndex.Clutch, L.Get("Pedal_Clutch")),
            new(session, PedalIndex.Brake, L.Get("Pedal_Brake")),
            new(session, PedalIndex.Throttle, L.Get("Pedal_Throttle")),
        };

        CombinedSeries = new ISeries[]
        {
            new LineSeries<ObservableValue> { Name = L.Get("Pedal_Clutch"), Values = ClutchSamples, GeometrySize = 0 },
            new LineSeries<ObservableValue> { Name = L.Get("Pedal_Brake"), Values = BrakeSamples, GeometrySize = 0 },
            new LineSeries<ObservableValue> { Name = L.Get("Pedal_Throttle"), Values = ThrottleSamples, GeometrySize = 0 },
        };
        _session.StateReceived += OnState;

        _isConnected = session.IsConnected;
        _session.Connected += OnConnectionChanged;
        _session.Disconnected += OnConnectionChanged;
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => IsConnected = _session.IsConnected;

    private void OnState(object? sender, PedalState state)
    {
        Append(ClutchSamples, state.Clutch.Output / 65535.0);
        Append(BrakeSamples, state.Brake.Output / 65535.0);
        Append(ThrottleSamples, state.Throttle.Output / 65535.0);
    }

    private static void Append(ObservableCollection<ObservableValue> series, double value)
    {
        series.Add(new ObservableValue(value));
        if (series.Count > MaxSamples)
            series.RemoveAt(0);
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        await _session.ConnectAsync();
        foreach (var column in Columns)
            await column.LoadAsync();
    }

    private bool CanConnect() => !IsConnected;

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task DisconnectAsync() => _session.DisconnectAsync();

    [RelayCommand(CanExecute = nameof(CanSave))]
    private Task SaveToControllerAsync() => _session.SendCommandAsync(PedalCommandId.SaveToFlash);

    private bool CanSave() => IsConnected && _session.SupportsConfig;

    [ObservableProperty] private bool _isCalibrating;

    [RelayCommand] private void OpenCalibration() => IsCalibrating = true;

    [RelayCommand]
    private void CloseCalibration()
    {
        foreach (var c in Columns) c.EndCapture();
        IsCalibrating = false;
    }

    [RelayCommand]
    private async Task StartCalibration()
    {
        foreach (var c in Columns) c.BeginCapture();
        foreach (PedalIndex p in Enum.GetValues<PedalIndex>())
            await _session.SendCommandAsync(PedalCommandId.CalibrateStart, (byte)p);
    }

    [RelayCommand]
    private async Task FinishCalibration()
    {
        foreach (var c in Columns) c.EndCapture();
        foreach (PedalIndex p in Enum.GetValues<PedalIndex>())
            await _session.SendCommandAsync(PedalCommandId.CalibrateStop, (byte)p);
        foreach (var c in Columns)
            await c.LoadAsync();
    }

    [RelayCommand]
    private Task SavePreferencesAsync() => _storage.SaveAsync(ExportProfile());

    [RelayCommand]
    private async Task LoadPreferencesAsync()
    {
        var profile = await _storage.LoadAsync();
        if (profile is not null)
            ApplyProfile(profile);
    }

    public PedalProfile ExportProfile() => new(
        Columns.Select(c => new PedalProfileColumn(
            c.SensorType,
            c.InputMin,
            c.InputMax,
            c.Invert,
            (int)c.Smooth,
            c.Points.Select(p => p.Value).ToArray(),
            c.LoadCellScale,
            c.LoadCellMaxKg,
            c.BrakeUnitKg)).ToArray());

    public void ApplyProfile(PedalProfile profile)
    {
        for (var i = 0; i < Columns.Count && i < profile.Columns.Length; i++)
        {
            var col = Columns[i];
            var src = profile.Columns[i];
            col.SensorType = src.Sensor;
            col.Invert = src.Invert;
            col.Smooth = src.Smooth;
            col.InputMin = src.InputMin;
            col.InputMax = src.InputMax;
            col.LoadCellScale = src.LoadCellScale;
            col.LoadCellMaxKg = src.LoadCellMaxKg;
            col.BrakeUnitKg = src.BrakeUnitKg;
            for (var p = 0; p < col.Points.Count && p < src.Curve.Length; p++)
                col.Points[p].Value = src.Curve[p];
        }
    }

    public override void Dispose()
    {
        _session.StateReceived -= OnState;
        _session.Connected -= OnConnectionChanged;
        _session.Disconnected -= OnConnectionChanged;
        foreach (var column in Columns)
            column.Dispose();
        _session.Dispose();
        base.Dispose();
    }
}
