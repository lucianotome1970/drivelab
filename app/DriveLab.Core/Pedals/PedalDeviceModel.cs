// ============================================================================
//  DriveLab
//  PedalDeviceModel.cs — Motor device-side dos 3 pedais: settings por pedal, calibração e montagem do PedalState.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;

namespace DriveLab.Core.Pedals;

/// <summary>Motor device-side de 3 pedais: settings por (id,pedal), calibração e montagem do PedalState.
/// Não é thread-safe; o transporte que o envolve serializa o acesso.</summary>
public sealed class PedalDeviceModel
{
    private readonly Dictionary<(PedalSettingId, PedalIndex), SettingValue> _settings = new();
    private readonly VirtualPedal[] _pedals = { new(), new(), new() };
    private readonly bool[] _calibrating = new bool[3];
    private readonly ushort[] _calMin = new ushort[3];
    private readonly ushort[] _calMax = new ushort[3];

    public void SeedDefaults()
    {
        _settings.Clear();
        foreach (var d in PedalSettingsSchema.All)
            foreach (PedalIndex pedal in Enum.GetValues<PedalIndex>())
                _settings[(d.Id, pedal)] = new SettingValue(d.Type, d.Default);
        foreach (PedalIndex pedal in Enum.GetValues<PedalIndex>())
            foreach (var d in PedalSettingsSchema.All)
                ApplySetting(pedal, d.Id, _settings[(d.Id, pedal)].AsDouble);
    }

    public void LoadDefaults() => SeedDefaults();

    public void WriteSetting(PedalSettingId id, PedalIndex pedal, SettingValue value)
    {
        var d = PedalSettingsSchema.Get(id);
        var clamped = new SettingValue(d.Type, d.Clamp(value.AsDouble));
        _settings[(id, pedal)] = clamped;
        ApplySetting(pedal, id, clamped.AsDouble);
    }

    public SettingValue ReadSetting(PedalSettingId id, PedalIndex pedal) => _settings[(id, pedal)];

    public void SetRawInputs(ushort clutch, ushort brake, ushort throttle)
    {
        _pedals[0].SetRawInput(clutch);
        _pedals[1].SetRawInput(brake);
        _pedals[2].SetRawInput(throttle);
        TrackCalibration();
    }

    public void CalibrateStart(PedalIndex pedal)
    {
        var i = (int)pedal;
        _calibrating[i] = true;
        _calMin[i] = ushort.MaxValue;
        _calMax[i] = ushort.MinValue;
    }

    public void CalibrateStop(PedalIndex pedal)
    {
        var i = (int)pedal;
        if (!_calibrating[i]) return;
        _calibrating[i] = false;
        if (_calMax[i] < _calMin[i]) return; // nenhuma amostra observada
        var d = PedalSettingsSchema.Get(PedalSettingId.InputMin);
        _settings[(PedalSettingId.InputMin, pedal)] = new SettingValue(d.Type, _calMin[i]);
        _settings[(PedalSettingId.InputMax, pedal)] = new SettingValue(d.Type, _calMax[i]);
        ApplySetting(pedal, PedalSettingId.InputMin, _calMin[i]);
        ApplySetting(pedal, PedalSettingId.InputMax, _calMax[i]);
    }

    public PedalState BuildState(FirmwareVersion firmware, byte flags) => new()
    {
        Firmware = firmware,
        Flags = flags,
        Clutch = new PedalReading(_pedals[0].RawInput, _pedals[0].Output),
        Brake = new PedalReading(_pedals[1].RawInput, _pedals[1].Output),
        Throttle = new PedalReading(_pedals[2].RawInput, _pedals[2].Output),
    };

    private void ApplySetting(PedalIndex pedal, PedalSettingId id, double value)
    {
        var p = _pedals[(int)pedal];
        switch (id)
        {
            case PedalSettingId.SensorType: p.SensorType = (byte)value; break;
            case PedalSettingId.InputMin: p.InputMin = (ushort)value; break;
            case PedalSettingId.InputMax: p.InputMax = (ushort)value; break;
            case PedalSettingId.Invert: p.Invert = value != 0; break;
            case PedalSettingId.Smooth: p.Smooth = (byte)value; break;
            case PedalSettingId.LoadCellScale: p.LoadCellScale = (ushort)value; break;
            case PedalSettingId.DeadzoneLow: p.DeadzoneLow = (byte)value; break;
            case PedalSettingId.DeadzoneHigh: p.DeadzoneHigh = (byte)value; break;
            default:
                if (id >= PedalSettingId.CurvePoint0 && id <= PedalSettingId.CurvePoint5)
                    p.CurvePoints[id - PedalSettingId.CurvePoint0] = value;
                break;
        }
    }

    private void TrackCalibration()
    {
        for (var i = 0; i < _pedals.Length; i++)
        {
            if (!_calibrating[i]) continue;
            var raw = _pedals[i].RawInput;
            if (raw < _calMin[i]) _calMin[i] = raw;
            if (raw > _calMax[i]) _calMax[i] = raw;
        }
    }
}
