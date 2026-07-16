// ============================================================================
//  DriveLab
//  HandbrakeViewModel.cs — VM do freio de mão: eixo único e botão digital, espelhando PedalColumnViewModel.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

/// <summary>VM do freio de mão: eixo único (sem PedalIndex) + botão digital.
/// Espelha o subconjunto de eixo único de <see cref="PedalColumnViewModel"/> e expõe
/// o contrato completo de binding do <c>PedalCurveEditor</c> (Points/Deadzone/Live/CanEdit).</summary>
public sealed partial class HandbrakeViewModel : ViewModelBase
{
    private readonly HandbrakeDeviceSession _session;
    private readonly IHandbrakeProfileStorage _storage;
    private bool _loading;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isConnected;
    [ObservableProperty] private string _sourceLabel = "";
    [ObservableProperty] private bool _canEdit;

    /// <summary>App difere da flash da placa (alteração não salva) — habilita Salvar; zera ao carregar/salvar.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isDirty;

    /// <summary>Só no simulador aparecem os botões Conectar/Desconectar (no real é automático).</summary>
    public bool IsSimulator { get; }

    [ObservableProperty] private double _currentInput01;
    [ObservableProperty] private double _currentOutput01;

    [ObservableProperty] private int _sensorType;
    [ObservableProperty] private bool _invert;
    [ObservableProperty] private double _smooth;
    [ObservableProperty] private int _inputMin;
    [ObservableProperty] private int _inputMax = 4095;
    [ObservableProperty] private int _loadCellScale = 1000;
    [ObservableProperty] private int _deadzoneLow;
    [ObservableProperty] private int _deadzoneHigh = 100;

    [ObservableProperty] private bool _buttonActive;
    [ObservableProperty] private bool _buttonEnabled = true;
    [ObservableProperty] private int _buttonThreshold = 70;

    public IReadOnlyList<PedalCurvePointViewModel> Points { get; }

    public HandbrakeViewModel(HandbrakeDeviceSession session, IHandbrakeProfileStorage storage, bool simulatorMode = false)
    {
        _session = session;
        _storage = storage;
        IsSimulator = simulatorMode;
        SourceLabel = session.SourceLabel;

        var labels = new[] { "0%", "20%", "40%", "60%", "80%", "100%" };
        Points = HandbrakeSettingsSchema.CurvePointIds
            .Select((id, i) => new PedalCurvePointViewModel(
                i, labels[i], HandbrakeSettingsSchema.Get(id).Default, OnPointChanged))
            .ToList();

        _canEdit = _session.IsConnected;
        _isConnected = _session.IsConnected;
        _session.StateReceived += OnState;
        _session.Connected += OnConnectionChanged;
        _session.Disconnected += OnConnectionChanged;
        _session.SettingChanged += OnSettingChanged;
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        await _session.ConnectAsync();
        await LoadAsync();
    }

    private bool CanConnect() => !IsConnected;

    [RelayCommand(CanExecute = nameof(IsConnected))]
    private Task DisconnectAsync() => _session.DisconnectAsync();

    public async Task LoadAsync()
    {
        if (!_session.IsConnected)
            return;

        _loading = true;
        SensorType = (int)(await _session.ReadSettingAsync(HandbrakeSettingId.SensorType)).AsDouble;
        Invert = (await _session.ReadSettingAsync(HandbrakeSettingId.Invert)).AsDouble != 0;
        Smooth = (await _session.ReadSettingAsync(HandbrakeSettingId.Smooth)).AsDouble;
        for (var i = 0; i < Points.Count; i++)
        {
            var v = await _session.ReadSettingAsync(HandbrakeSettingsSchema.CurvePointIds[i]);
            Points[i].SetQuiet(v.AsDouble);
        }
        InputMin = (int)(await _session.ReadSettingAsync(HandbrakeSettingId.InputMin)).AsDouble;
        InputMax = (int)(await _session.ReadSettingAsync(HandbrakeSettingId.InputMax)).AsDouble;
        LoadCellScale = (int)(await _session.ReadSettingAsync(HandbrakeSettingId.LoadCellScale)).AsDouble;
        DeadzoneLow = (int)(await _session.ReadSettingAsync(HandbrakeSettingId.DeadzoneLow)).AsDouble;
        DeadzoneHigh = (int)(await _session.ReadSettingAsync(HandbrakeSettingId.DeadzoneHigh)).AsDouble;
        ButtonEnabled = (await _session.ReadSettingAsync(HandbrakeSettingId.ButtonEnabled)).AsDouble != 0;
        ButtonThreshold = (int)(await _session.ReadSettingAsync(HandbrakeSettingId.ButtonThreshold)).AsDouble;
        _loading = false;
        IsDirty = false;   // acabou de carregar da placa: app == flash
    }

    private void OnPointChanged(int index, double value)
    {
        if (_loading || !_session.IsConnected)
            return;
        var id = HandbrakeSettingsSchema.CurvePointIds[index];
        var d = HandbrakeSettingsSchema.Get(id);
        _ = _session.WriteSettingAsync(id, new SettingValue(d.Type, d.Clamp(value)));
        IsDirty = true;
    }

    private void WriteScalar(HandbrakeSettingId id, double value)
    {
        if (_loading || !_session.IsConnected)
            return;
        var d = HandbrakeSettingsSchema.Get(id);
        _ = _session.WriteSettingAsync(id, new SettingValue(d.Type, d.Clamp(value)));
        IsDirty = true;
    }

    partial void OnSensorTypeChanged(int value) => WriteScalar(HandbrakeSettingId.SensorType, value);
    partial void OnInvertChanged(bool value) => WriteScalar(HandbrakeSettingId.Invert, value ? 1 : 0);
    partial void OnSmoothChanged(double value) => WriteScalar(HandbrakeSettingId.Smooth, value);
    partial void OnInputMinChanged(int value) => WriteScalar(HandbrakeSettingId.InputMin, value);
    partial void OnInputMaxChanged(int value) => WriteScalar(HandbrakeSettingId.InputMax, value);
    partial void OnLoadCellScaleChanged(int value) => WriteScalar(HandbrakeSettingId.LoadCellScale, value);
    partial void OnDeadzoneLowChanged(int value) => WriteScalar(HandbrakeSettingId.DeadzoneLow, value);
    partial void OnDeadzoneHighChanged(int value) => WriteScalar(HandbrakeSettingId.DeadzoneHigh, value);
    partial void OnButtonEnabledChanged(bool value) => WriteScalar(HandbrakeSettingId.ButtonEnabled, value ? 1 : 0);
    partial void OnButtonThresholdChanged(int value) => WriteScalar(HandbrakeSettingId.ButtonThreshold, value);

    [RelayCommand]
    private Task CalibrateStart() => _session.SendCommandAsync(PedalCommandId.CalibrateStart);

    [RelayCommand]
    private async Task CalibrateStop()
    {
        await _session.SendCommandAsync(PedalCommandId.CalibrateStop);
        IsDirty = true;   // calibração mudou min/max na placa (RAM) — precisa salvar na flash
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task Save()
    {
        await _session.SendCommandAsync(PedalCommandId.SaveToFlash);
        IsDirty = false;  // gravou na flash: firmware == app
    }

    private bool CanSave() => IsConnected && IsDirty;

    [RelayCommand]
    private Task LoadDefaults() => _session.SendCommandAsync(PedalCommandId.LoadDefaults);

    [RelayCommand]
    private void SelectSensor(string type) => SensorType = int.Parse(type);

    private void OnConnectionChanged(object? sender, EventArgs e)
    {
        IsConnected = _session.IsConnected;
        CanEdit = _session.IsConnected;
    }

    private void OnState(object? sender, PedalState state)
    {
        CurrentInput01 = state.Clutch.RawInput / 4095.0;
        CurrentOutput01 = state.Clutch.Output / 65535.0;
        ButtonActive = (state.Flags & (byte)HandbrakeFlags.ButtonPressed) != 0;
        IsConnected = _session.IsConnected;
    }

    private void OnSettingChanged(object? sender, HandbrakeSettingChangedEventArgs e)
    {
        _loading = true;
        switch (e.Id)
        {
            case HandbrakeSettingId.SensorType: SensorType = (int)e.Value.AsDouble; break;
            case HandbrakeSettingId.Invert: Invert = e.Value.AsDouble != 0; break;
            case HandbrakeSettingId.Smooth: Smooth = e.Value.AsDouble; break;
            case HandbrakeSettingId.InputMin: InputMin = (int)e.Value.AsDouble; break;
            case HandbrakeSettingId.InputMax: InputMax = (int)e.Value.AsDouble; break;
            case HandbrakeSettingId.LoadCellScale: LoadCellScale = (int)e.Value.AsDouble; break;
            case HandbrakeSettingId.DeadzoneLow: DeadzoneLow = (int)e.Value.AsDouble; break;
            case HandbrakeSettingId.DeadzoneHigh: DeadzoneHigh = (int)e.Value.AsDouble; break;
            case HandbrakeSettingId.ButtonEnabled: ButtonEnabled = e.Value.AsDouble != 0; break;
            case HandbrakeSettingId.ButtonThreshold: ButtonThreshold = (int)e.Value.AsDouble; break;
            default:
                if (e.Id >= HandbrakeSettingId.CurvePoint0 && e.Id <= HandbrakeSettingId.CurvePoint5)
                    Points[e.Id - HandbrakeSettingId.CurvePoint0].SetQuiet(e.Value.AsDouble);
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
        _session.Dispose();
        base.Dispose();
    }
}
