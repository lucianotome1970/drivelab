using System.Threading;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Simulator;

public sealed class SimulatorPedalTransport : IPedalTransport
{
    private readonly object _sync = new();
    private readonly Dictionary<(PedalSettingId, PedalIndex), SettingValue> _settings = new();
    private readonly VirtualPedal[] _pedals = { new(), new(), new() };
    private Timer? _timer;
    private int _periodMs;
    private volatile bool _streaming;
    private int _phase;
    private readonly bool[] _calibrating = new bool[3];
    private readonly ushort[] _calMin = new ushort[3];
    private readonly ushort[] _calMax = new ushort[3];

    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion { get; } = new(0, 26, 7, 13);
    public byte Flags { get; private set; }

    public event EventHandler<PedalState>? StateReceived;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            SeedDefaults();
        }
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        StopStreaming();
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task WriteSettingAsync(PedalSettingId id, PedalIndex pedal, SettingValue value)
    {
        lock (_sync)
        {
            var d = PedalSettingsSchema.Get(id);
            var clamped = new SettingValue(d.Type, d.Clamp(value.AsDouble));
            _settings[(id, pedal)] = clamped;
            ApplySetting(pedal, id, clamped.AsDouble);
        }
        return Task.CompletedTask;
    }

    public Task<SettingValue> ReadSettingAsync(PedalSettingId id, PedalIndex pedal)
    {
        lock (_sync)
        {
            return Task.FromResult(_settings[(id, pedal)]);
        }
    }

    public Task SendCommandAsync(PedalCommandId command, byte arg = 0)
    {
        lock (_sync)
        {
            switch (command)
            {
                case PedalCommandId.LoadDefaults:
                    SeedDefaults();
                    break;
                case PedalCommandId.CalibrateStart:
                    if (arg < 3)
                    {
                        _calibrating[arg] = true;
                        _calMin[arg] = ushort.MaxValue;
                        _calMax[arg] = ushort.MinValue;
                    }
                    break;
                case PedalCommandId.CalibrateStop:
                    if (arg < 3 && _calibrating[arg])
                    {
                        _calibrating[arg] = false;
                        var pedal = (PedalIndex)arg;
                        if (_calMax[arg] >= _calMin[arg])
                        {
                            var d = PedalSettingsSchema.Get(PedalSettingId.InputMin);
                            _settings[(PedalSettingId.InputMin, pedal)] = new SettingValue(d.Type, _calMin[arg]);
                            _settings[(PedalSettingId.InputMax, pedal)] = new SettingValue(d.Type, _calMax[arg]);
                            ApplySetting(pedal, PedalSettingId.InputMin, _calMin[arg]);
                            ApplySetting(pedal, PedalSettingId.InputMax, _calMax[arg]);
                        }
                    }
                    break;
                case PedalCommandId.SaveToFlash:
                    break;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Test/demo hook: injeta leituras cruas nos 3 pedais.</summary>
    public void SetRawInputs(ushort clutch, ushort brake, ushort throttle)
    {
        lock (_sync)
        {
            _pedals[0].SetRawInput(clutch);
            _pedals[1].SetRawInput(brake);
            _pedals[2].SetRawInput(throttle);
            TrackCalibration();
        }
    }

    public void Step()
    {
        PedalState state;
        lock (_sync)
        {
            if (_streaming)
            {
                _phase++;
                for (var i = 0; i < _pedals.Length; i++)
                    _pedals[i].SetRawInput(GenerateRaw(_phase + i * 40));
                TrackCalibration();
            }
            state = BuildState();
        }
        StateReceived?.Invoke(this, state);
    }

    public void StartStreaming(int hz = 60)
    {
        StopStreaming();
        _periodMs = Math.Max(1, 1000 / hz);
        _streaming = true;
        _timer = new Timer(_ =>
        {
            if (!_streaming) return;
            Step();
        }, null, _periodMs, _periodMs);
    }

    public void StopStreaming()
    {
        _streaming = false;
        _timer?.Dispose();
        _timer = null;
    }

    private void SeedDefaults()
    {
        _settings.Clear();
        foreach (var d in PedalSettingsSchema.All)
            foreach (PedalIndex pedal in Enum.GetValues<PedalIndex>())
                _settings[(d.Id, pedal)] = new SettingValue(d.Type, d.Default);
        foreach (PedalIndex pedal in Enum.GetValues<PedalIndex>())
            foreach (var d in PedalSettingsSchema.All)
                ApplySetting(pedal, d.Id, _settings[(d.Id, pedal)].AsDouble);
    }

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

    private static ushort GenerateRaw(int phase)
    {
        var t = (phase % 120) / 120.0;          // sawtooth 0..1
        var tri = t < 0.5 ? t * 2 : (1 - t) * 2; // triângulo 0..1..0
        return (ushort)Math.Round(tri * 4095);
    }

    private PedalState BuildState() => new()
    {
        Firmware = FirmwareVersion,
        Flags = Flags,
        Clutch = new PedalReading(_pedals[0].RawInput, _pedals[0].Output),
        Brake = new PedalReading(_pedals[1].RawInput, _pedals[1].Output),
        Throttle = new PedalReading(_pedals[2].RawInput, _pedals[2].Output),
    };
}
