using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;

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
    public IReadOnlyList<PedalCurvePreset> Presets => PedalCurvePresets.All;

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
            new(session, PedalIndex.Clutch, "Embreagem"),
            new(session, PedalIndex.Brake, "Freio"),
            new(session, PedalIndex.Throttle, "Acelerador"),
        };

        CombinedSeries = new ISeries[]
        {
            new LineSeries<ObservableValue> { Name = "Embreagem", Values = ClutchSamples, GeometrySize = 0 },
            new LineSeries<ObservableValue> { Name = "Freio", Values = BrakeSamples, GeometrySize = 0 },
            new LineSeries<ObservableValue> { Name = "Acelerador", Values = ThrottleSamples, GeometrySize = 0 },
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

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task SaveToControllerAsync() => _session.SendCommandAsync(PedalCommandId.SaveToFlash);

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
            c.LoadCellScale)).ToArray());

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
