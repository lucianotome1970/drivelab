// ============================================================================
//  DriveLab
//  SimulatorTransport.cs — Transporte simulado do volante: aplica settings a um VirtualWheel e gera BaseState sintético.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Threading;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Simulator;

public sealed class SimulatorTransport : IBaseTransport
{
    private readonly object _sync = new();
    private readonly Dictionary<BaseSettingId, SettingValue> _settings = new();
    private readonly VirtualWheel _wheel = new();
    private Timer? _timer;
    private int _periodMs;
    private volatile bool _streaming;

    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion { get; } = new(0, 26, 7, 12);
    public BaseFlags Flags { get; private set; } = BaseFlags.UsingSimulator | BaseFlags.Calibrated;

    public event EventHandler<BaseState>? StateReceived;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        lock (_sync)
        {
            _settings.Clear();
            foreach (var descriptor in BaseSettingsSchema.All)
                _settings[descriptor.Id] = new SettingValue(descriptor.Type, descriptor.Default);

            ApplySettingsToWheel();
            _wheel.ForceEnabled = Flags.HasFlag(BaseFlags.ForceEnabled);
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

    public Task WriteSettingAsync(BaseSettingId id, SettingValue value)
    {
        lock (_sync)
        {
            var descriptor = BaseSettingsSchema.Get(id);
            var clamped = new SettingValue(descriptor.Type, descriptor.Clamp(value.AsDouble));
            _settings[id] = clamped;
            ApplySettingsToWheel();
        }
        return Task.CompletedTask;
    }

    public Task<SettingValue> ReadSettingAsync(BaseSettingId id)
    {
        lock (_sync)
        {
            return Task.FromResult(_settings[id]);
        }
    }

    public Task SendDirectControlAsync(BaseDirectControl control)
    {
        lock (_sync)
        {
            _wheel.SetInputs(
                constant: control.ConstantForce / 10000.0,
                spring: control.SpringForce / 10000.0,
                periodic: control.PeriodicForce / 10000.0,
                damper: control.DamperForce / 10000.0,
                forceDrop01: control.ForceDrop / 100.0);
        }
        return Task.CompletedTask;
    }

    public Task SendCommandAsync(BaseCommand command, byte arg = 0)
    {
        switch (command)
        {
            case BaseCommand.ResetCenter:
                lock (_sync)
                {
                    _wheel.ResetCenter();
                }
                break;
            case BaseCommand.SetForceEnabled:
                lock (_sync)
                {
                    _wheel.ForceEnabled = arg != 0;
                }
                Flags = arg != 0 ? Flags | BaseFlags.ForceEnabled : Flags & ~BaseFlags.ForceEnabled;
                break;
            case BaseCommand.Reboot:
                return ConnectAsync();
            case BaseCommand.SaveSettings:
            case BaseCommand.EnterDfu:
            case BaseCommand.Calibrate:
                break;
        }
        return Task.CompletedTask;
    }

    public void Step(double dt)
    {
        BaseState state;
        lock (_sync)
        {
            _wheel.Step(dt);
            state = BuildState();
        }
        StateReceived?.Invoke(this, state);
    }

    public void StartStreaming(int hz = 100)
    {
        StopStreaming();
        _periodMs = Math.Max(1, 1000 / hz);
        _streaming = true;
        _timer = new Timer(_ =>
        {
            if (!_streaming) return;
            Step(_periodMs / 1000.0);
        }, null, _periodMs, _periodMs);
    }

    public void StopStreaming()
    {
        _streaming = false;
        _timer?.Dispose();
        _timer = null;
    }

    private void ApplySettingsToWheel()
    {
        _wheel.MotionRangeDeg = _settings[BaseSettingId.MotionRange].AsDouble;
        _wheel.SpringGain = _settings[BaseSettingId.SpringStrength].AsDouble / 100.0;
        _wheel.DamperGain = Math.Max(_settings[BaseSettingId.DamperStrength].AsDouble / 100.0, 0.01);
        _wheel.TotalStrength01 = _settings[BaseSettingId.TotalStrength].AsDouble / 100.0;
    }

    private BaseState BuildState() => new()
    {
        Firmware = FirmwareVersion,
        Flags = Flags,
        Position = _wheel.PositionNormalized,
        AngleDeciDeg = _wheel.AngleDeciDeg,
        Torque = _wheel.TorqueNormalized,
        MotorCurrentMa = (short)(_wheel.TorqueNormalized / 2),
        // Synthetic placeholder telemetry: real values come from firmware at M2.5.
        BusVoltageMv = 24000,
        FetTempC = 38,
        MotorTempC = 42,
        McuTempC = 45,
        ErrorCode = 0,
    };
}
