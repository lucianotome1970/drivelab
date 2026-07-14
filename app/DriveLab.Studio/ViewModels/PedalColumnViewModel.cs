using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace DriveLab.Studio.ViewModels;

public sealed partial class PedalCurvePointViewModel : ObservableObject
{
    private readonly Action<int, double> _onChanged;
    private bool _loading;

    public int Index { get; }
    public string Label { get; }

    [ObservableProperty]
    private double _value;

    public PedalCurvePointViewModel(int index, string label, double value, Action<int, double> onChanged)
    {
        Index = index;
        Label = label;
        _value = value;
        _onChanged = onChanged;
    }

    /// <summary>Atualiza o valor sem disparar o write de volta (evita eco).</summary>
    public void SetQuiet(double value)
    {
        _loading = true;
        Value = value;
        _loading = false;
    }

    partial void OnValueChanged(double value)
    {
        if (!_loading)
            _onChanged(Index, value);
    }
}

public sealed partial class PedalColumnViewModel : ViewModelBase
{
    private const int CurveSamples = 21;
    private readonly PedalDeviceSession _session;

    [ObservableProperty] private int _sensorType;
    [ObservableProperty] private bool _invert;
    [ObservableProperty] private double _smooth;
    [ObservableProperty] private bool _canEdit;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentForceKg))]
    [NotifyPropertyChangedFor(nameof(BrakeReadout))]
    [NotifyPropertyChangedFor(nameof(ForceMaxed))]
    [NotifyPropertyChangedFor(nameof(RawLive))]
    private double _currentInput01;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentOutputPercent))]
    [NotifyPropertyChangedFor(nameof(BrakeReadout))]
    private double _currentOutput01;

    /// <summary>Avanço do pedal (0–100) para a barra de progressão vertical.</summary>
    public double CurrentOutputPercent => CurrentOutput01 * 100.0;
    [ObservableProperty] private int _inputMin;
    [ObservableProperty] private int _inputMax = 4095;
    [ObservableProperty] private int _loadCellScale = 1000;
    [ObservableProperty] private int _deadzoneLow;
    [ObservableProperty] private int _deadzoneHigh = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentForceKg))]
    [NotifyPropertyChangedFor(nameof(BrakeReadout))]
    private int _loadCellMaxKg = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BrakeReadout))]
    private bool _brakeUnitKg;

    /// <summary>Freio: exibe força em KG e o seletor de fundo de escala.</summary>
    public bool IsBrake => Pedal == PedalIndex.Brake;

    /// <summary>Força física (input cru × fundo de escala) — separada da posição do eixo.</summary>
    public double CurrentForceKg => CurrentInput01 * LoadCellMaxKg;
    public bool ForceMaxed => CurrentInput01 >= 0.99;
    public string BrakeReadout => BrakeUnitKg ? $"{CurrentForceKg:0} kg" : $"{CurrentOutput01 * 100:0}%";

    /// <summary>Valor cru ao vivo (0–4095) para o modal de calibração.</summary>
    public int RawLive => (int)Math.Round(CurrentInput01 * 4095.0);

    /// <summary>Min/max cru observado durante a captura de calibração (mostra o "ponto de calibragem").</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalRange))]
    private int _calMin;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalRange))]
    private int _calMax;

    [ObservableProperty] private bool _capturing;

    public string CalRange => $"{CalMin} – {CalMax}";

    /// <summary>Começa a acumular o min/max cru (chamado no Start da calibração).</summary>
    public void BeginCapture()
    {
        CalMin = 4095; // sentinela: o 1º sample de telemetria define o min real
        CalMax = 0;
        Capturing = true;
    }

    /// <summary>Para de acumular (chamado no Finish/Fechar).</summary>
    public void EndCapture() => Capturing = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CalibrateLabel))]
    private bool _isCalibrating;

    public string CalibrateLabel => Localization.LocalizationManager.Get(IsCalibrating ? "Pedal_StopCalibration" : "Pedal_StartCalibration");

    private bool _loading;

    public string Label { get; }
    public PedalIndex Pedal { get; }
    public IReadOnlyList<PedalCurvePointViewModel> Points { get; }

    /// <summary>Presets de curva desta coluna, como chips selecionáveis (estilo Pit House).</summary>
    public IReadOnlyList<PedalPresetOption> PresetOptions { get; }

    /// <summary>Tipo de sensor só faz sentido em fonte configurável (nossos pedais); oculto p/ read-only (Simagic).</summary>
    public bool ShowSensor => _session.SupportsConfig;

    /// <summary>Em fonte read-only, a curva editada é só pré-visualização (não vai ao dispositivo).</summary>
    public bool CurvePreviewOnly => !_session.SupportsConfig;

    public ObservableCollection<ObservablePoint> CurveValues { get; } = new();
    public ObservableCollection<ObservablePoint> IdentityValues { get; } = new();
    public ObservableCollection<ObservablePoint> LiveValues { get; } = new();
    public ISeries[] CurveSeries { get; }

    public PedalColumnViewModel(PedalDeviceSession session, PedalIndex pedal, string label)
    {
        _session = session;
        Pedal = pedal;
        Label = label;

        var labels = new[] { "0%", "20%", "40%", "60%", "80%", "100%" };
        Points = PedalSettingsSchema.CurvePointIds
            .Select((id, i) => new PedalCurvePointViewModel(
                i, labels[i], PedalSettingsSchema.Get(id).Default, OnPointChanged))
            .ToList();

        PresetOptions = PedalCurvePresets.All.Select(p => new PedalPresetOption(p)).ToList();

        IdentityValues.Add(new ObservablePoint(0, 0));
        IdentityValues.Add(new ObservablePoint(1, 1));

        var accent = new SKColor(0xFF, 0x6A, 0x00);
        CurveSeries = new ISeries[]
        {
            new LineSeries<ObservablePoint>
            {
                Name = "Identidade", Values = IdentityValues, GeometrySize = 0, IsHoverable = false,
                Stroke = new SolidColorPaint(new SKColor(0x26, 0x2C, 0x34)) { StrokeThickness = 1 }, Fill = null,
            },
            new LineSeries<ObservablePoint>
            {
                Name = "Curva", Values = CurveValues, GeometrySize = 0,
                Stroke = new SolidColorPaint(accent) { StrokeThickness = 3 }, Fill = null,
            },
            new ScatterSeries<ObservablePoint>
            {
                Name = "Atual", Values = LiveValues, GeometrySize = 13,
                Fill = new SolidColorPaint(accent),
            },
        };

        RebuildCurve();

        _canEdit = _session.IsConnected;
        _session.StateReceived += OnState;
        _session.Connected += OnConnectionChanged;
        _session.Disconnected += OnConnectionChanged;
        _session.SettingChanged += OnSettingChanged;
    }

    public void ApplyPreset(PedalCurvePreset preset)
    {
        for (var i = 0; i < Points.Count && i < preset.Points.Length; i++)
            Points[i].Value = preset.Points[i]; // dispara write (se conectado) + rebuild
    }

    [RelayCommand]
    private void SelectPreset(PedalCurvePreset preset)
    {
        foreach (var option in PresetOptions)
            option.IsSelected = ReferenceEquals(option.Preset, preset);
        ApplyPreset(preset);
    }

    [RelayCommand]
    private void SelectSensor(string type) => SensorType = int.Parse(type);

    public async Task LoadAsync()
    {
        if (!_session.IsConnected)
            return;

        _loading = true;
        SensorType = (int)(await _session.ReadSettingAsync(PedalSettingId.SensorType, Pedal)).AsDouble;
        Invert = (await _session.ReadSettingAsync(PedalSettingId.Invert, Pedal)).AsDouble != 0;
        Smooth = (await _session.ReadSettingAsync(PedalSettingId.Smooth, Pedal)).AsDouble;
        for (var i = 0; i < Points.Count; i++)
        {
            var v = await _session.ReadSettingAsync(PedalSettingsSchema.CurvePointIds[i], Pedal);
            Points[i].SetQuiet(v.AsDouble);
        }
        InputMin = (int)(await _session.ReadSettingAsync(PedalSettingId.InputMin, Pedal)).AsDouble;
        InputMax = (int)(await _session.ReadSettingAsync(PedalSettingId.InputMax, Pedal)).AsDouble;
        LoadCellScale = (int)(await _session.ReadSettingAsync(PedalSettingId.LoadCellScale, Pedal)).AsDouble;
        DeadzoneLow = (int)(await _session.ReadSettingAsync(PedalSettingId.DeadzoneLow, Pedal)).AsDouble;
        DeadzoneHigh = (int)(await _session.ReadSettingAsync(PedalSettingId.DeadzoneHigh, Pedal)).AsDouble;
        _loading = false;
        RebuildCurve();
    }

    public void RebuildCurve()
    {
        var pts = Points.Select(p => p.Value).ToList();
        CurveValues.Clear();
        for (var i = 0; i < CurveSamples; i++)
        {
            var x = (double)i / (CurveSamples - 1);
            CurveValues.Add(new ObservablePoint(x, PedalCurve.Evaluate(pts, x)));
        }
    }

    private void OnPointChanged(int index, double value)
    {
        RebuildCurve();
        if (_loading || !_session.IsConnected)
            return;
        var id = PedalSettingsSchema.CurvePointIds[index];
        var d = PedalSettingsSchema.Get(id);
        _ = _session.WriteSettingAsync(id, Pedal, new SettingValue(d.Type, d.Clamp(value)));
    }

    private void WriteScalar(PedalSettingId id, double value)
    {
        if (_loading || !_session.IsConnected)
            return;
        var d = PedalSettingsSchema.Get(id);
        _ = _session.WriteSettingAsync(id, Pedal, new SettingValue(d.Type, d.Clamp(value)));
    }

    partial void OnSensorTypeChanged(int value) => WriteScalar(PedalSettingId.SensorType, value);
    partial void OnInvertChanged(bool value) => WriteScalar(PedalSettingId.Invert, value ? 1 : 0);
    partial void OnSmoothChanged(double value) => WriteScalar(PedalSettingId.Smooth, value);
    partial void OnInputMinChanged(int value) => WriteScalar(PedalSettingId.InputMin, value);
    partial void OnInputMaxChanged(int value) => WriteScalar(PedalSettingId.InputMax, value);
    partial void OnLoadCellScaleChanged(int value) => WriteScalar(PedalSettingId.LoadCellScale, value);
    partial void OnDeadzoneLowChanged(int value) => WriteScalar(PedalSettingId.DeadzoneLow, value);
    partial void OnDeadzoneHighChanged(int value) => WriteScalar(PedalSettingId.DeadzoneHigh, value);

    [RelayCommand] private void SetBrakeUnit(string kg) => BrakeUnitKg = kg == "kg";
    [RelayCommand] private void SetLoadCellMax(string kg) => LoadCellMaxKg = int.Parse(kg);

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        if (!_session.IsConnected)
            return;

        if (!IsCalibrating)
        {
            IsCalibrating = true;
            await _session.SendCommandAsync(PedalCommandId.CalibrateStart, (byte)Pedal);
        }
        else
        {
            IsCalibrating = false;
            await _session.SendCommandAsync(PedalCommandId.CalibrateStop, (byte)Pedal);
            _loading = true;
            InputMin = (int)(await _session.ReadSettingAsync(PedalSettingId.InputMin, Pedal)).AsDouble;
            InputMax = (int)(await _session.ReadSettingAsync(PedalSettingId.InputMax, Pedal)).AsDouble;
            _loading = false;
        }
    }

    private void OnConnectionChanged(object? sender, EventArgs e) => CanEdit = _session.IsConnected;

    private void OnState(object? sender, PedalState state)
    {
        var reading = state[Pedal];
        CurrentInput01 = reading.RawInput / 4095.0;
        CurrentOutput01 = reading.Output / 65535.0;
        if (Capturing)
        {
            if (reading.RawInput < CalMin) CalMin = reading.RawInput;
            if (reading.RawInput > CalMax) CalMax = reading.RawInput;
        }
        LiveValues.Clear();
        LiveValues.Add(new ObservablePoint(CurrentInput01, CurrentOutput01));
    }

    private void OnSettingChanged(object? sender, PedalSettingChangedEventArgs e)
    {
        if (e.Pedal != Pedal)
            return;

        _loading = true;
        switch (e.Id)
        {
            case PedalSettingId.SensorType: SensorType = (int)e.Value.AsDouble; break;
            case PedalSettingId.Invert: Invert = e.Value.AsDouble != 0; break;
            case PedalSettingId.Smooth: Smooth = e.Value.AsDouble; break;
            case PedalSettingId.InputMin: InputMin = (int)e.Value.AsDouble; break;
            case PedalSettingId.InputMax: InputMax = (int)e.Value.AsDouble; break;
            case PedalSettingId.LoadCellScale: LoadCellScale = (int)e.Value.AsDouble; break;
            case PedalSettingId.DeadzoneLow: DeadzoneLow = (int)e.Value.AsDouble; break;
            case PedalSettingId.DeadzoneHigh: DeadzoneHigh = (int)e.Value.AsDouble; break;
            default:
                if (e.Id >= PedalSettingId.CurvePoint0 && e.Id <= PedalSettingId.CurvePoint5)
                {
                    Points[e.Id - PedalSettingId.CurvePoint0].SetQuiet(e.Value.AsDouble);
                    RebuildCurve();
                }
                break;
        }
        _loading = false;
    }

    public override void Dispose()
    {
        _session.StateReceived -= OnState;
        _session.Connected -= OnConnectionChanged;
        _session.Disconnected -= OnConnectionChanged;
        _session.SettingChanged -= OnSettingChanged;
        base.Dispose();
    }
}
