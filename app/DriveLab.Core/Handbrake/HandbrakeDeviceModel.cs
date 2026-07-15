// ============================================================================
//  DriveLab
//  HandbrakeDeviceModel.cs — Motor device-side do freio de mão: eixo único (pipeline P0) mais botão digital com histerese.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Pedals;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Core.Handbrake;

/// <summary>Motor device-side do freio de mão: 1 eixo (pipeline P0) + botão digital
/// com limiar/histerese. Não é thread-safe; o transporte que o envolve serializa o acesso.
/// Telemetria reusa PedalState: eixo no slot Clutch (índice 0), botão no bit
/// <see cref="HandbrakeFlags.ButtonPressed"/> de Flags.</summary>
public sealed class HandbrakeDeviceModel
{
    private const double HysteresisPercent = 3.0;

    private readonly Dictionary<HandbrakeSettingId, SettingValue> _settings = new();
    private readonly VirtualPedal _axis = new();
    private bool _calibrating;
    private ushort _calMin;
    private ushort _calMax;

    private double _threshold = 70;
    private bool _buttonEnabled = true;
    private bool _buttonPressed;

    public bool ButtonPressed => _buttonPressed;

    public void SeedDefaults()
    {
        _settings.Clear();
        foreach (var d in HandbrakeSettingsSchema.All)
            _settings[d.Id] = new SettingValue(d.Type, d.Default);
        foreach (var d in HandbrakeSettingsSchema.All)
            ApplySetting(d.Id, _settings[d.Id].AsDouble);
        _buttonPressed = false;
    }

    public void LoadDefaults() => SeedDefaults();

    public void WriteSetting(HandbrakeSettingId id, SettingValue value)
    {
        var d = HandbrakeSettingsSchema.Get(id);
        var clamped = new SettingValue(d.Type, d.Clamp(value.AsDouble));
        _settings[id] = clamped;
        ApplySetting(id, clamped.AsDouble);
    }

    public SettingValue ReadSetting(HandbrakeSettingId id) => _settings[id];

    public void SetRawInput(ushort raw)
    {
        _axis.SetRawInput(raw);
        TrackCalibration();
        UpdateButton();
    }

    public void CalibrateStart()
    {
        _calibrating = true;
        _calMin = ushort.MaxValue;
        _calMax = ushort.MinValue;
    }

    public void CalibrateStop()
    {
        if (!_calibrating) return;
        _calibrating = false;
        if (_calMax < _calMin) return; // nenhuma amostra observada
        var d = HandbrakeSettingsSchema.Get(HandbrakeSettingId.InputMin);
        _settings[HandbrakeSettingId.InputMin] = new SettingValue(d.Type, _calMin);
        _settings[HandbrakeSettingId.InputMax] = new SettingValue(d.Type, _calMax);
        ApplySetting(HandbrakeSettingId.InputMin, _calMin);
        ApplySetting(HandbrakeSettingId.InputMax, _calMax);
    }

    public PedalState BuildState(FirmwareVersion firmware, byte extraFlags = 0)
    {
        var flags = extraFlags;
        if (_buttonPressed) flags |= (byte)HandbrakeFlags.ButtonPressed;
        return new PedalState
        {
            Firmware = firmware,
            Flags = flags,
            Clutch = new PedalReading(_axis.RawInput, _axis.Output),
            Brake = new PedalReading(0, 0),
            Throttle = new PedalReading(0, 0),
        };
    }

    private void UpdateButton()
    {
        if (!_buttonEnabled) { _buttonPressed = false; return; }
        var outputPct = _axis.Output / 655.35; // 0..65535 -> 0..100
        if (_buttonPressed)
            _buttonPressed = outputPct >= _threshold - HysteresisPercent;
        else
            _buttonPressed = outputPct >= _threshold;
    }

    private void ApplySetting(HandbrakeSettingId id, double value)
    {
        switch (id)
        {
            case HandbrakeSettingId.SensorType: _axis.SensorType = (byte)value; break;
            case HandbrakeSettingId.InputMin: _axis.InputMin = (ushort)value; break;
            case HandbrakeSettingId.InputMax: _axis.InputMax = (ushort)value; break;
            case HandbrakeSettingId.Invert: _axis.Invert = value != 0; break;
            case HandbrakeSettingId.Smooth: _axis.Smooth = (byte)value; break;
            case HandbrakeSettingId.LoadCellScale: _axis.LoadCellScale = (ushort)value; break;
            case HandbrakeSettingId.DeadzoneLow: _axis.DeadzoneLow = (byte)value; break;
            case HandbrakeSettingId.DeadzoneHigh: _axis.DeadzoneHigh = (byte)value; break;
            case HandbrakeSettingId.ButtonThreshold: _threshold = value; break;
            case HandbrakeSettingId.ButtonEnabled: _buttonEnabled = value != 0; break;
            default:
                if (id >= HandbrakeSettingId.CurvePoint0 && id <= HandbrakeSettingId.CurvePoint5)
                    _axis.CurvePoints[id - HandbrakeSettingId.CurvePoint0] = value;
                break;
        }
    }

    private void TrackCalibration()
    {
        if (!_calibrating) return;
        var raw = _axis.RawInput;
        if (raw < _calMin) _calMin = raw;
        if (raw > _calMax) _calMax = raw;
    }
}
