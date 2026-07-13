using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Simulator;

public sealed class SimulatorTransport : ITransport
{
    private readonly Dictionary<SettingId, SettingValue> _settings = new();
    private readonly VirtualWheel _wheel = new();
    private DirectControl _control = new();

    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion { get; } = new(0, 26, 7, 12);
    public DeviceFlags Flags { get; private set; } = DeviceFlags.UsingSimulator | DeviceFlags.Calibrated;

    public event EventHandler<DeviceState>? StateReceived;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        _settings.Clear();
        foreach (var descriptor in SettingsSchema.All)
            _settings[descriptor.Id] = new SettingValue(descriptor.Type, descriptor.Default);

        ApplySettingsToWheel();
        IsConnected = true;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        return Task.CompletedTask;
    }

    public Task WriteSettingAsync(SettingId id, SettingValue value)
    {
        var descriptor = SettingsSchema.Get(id);
        var clamped = new SettingValue(descriptor.Type, descriptor.Clamp(value.AsDouble));
        _settings[id] = clamped;
        ApplySettingsToWheel();
        return Task.CompletedTask;
    }

    public Task<SettingValue> ReadSettingAsync(SettingId id) => Task.FromResult(_settings[id]);

    public Task SendDirectControlAsync(DirectControl control)
    {
        _control = control;
        _wheel.SetInputs(
            constant: control.ConstantForce / 10000.0,
            spring: control.SpringForce / 10000.0,
            periodic: control.PeriodicForce / 10000.0,
            damper: control.DamperForce / 10000.0,
            forceDrop01: control.ForceDrop / 100.0);
        return Task.CompletedTask;
    }

    public Task SendCommandAsync(DeviceCommand command, byte arg = 0)
    {
        switch (command)
        {
            case DeviceCommand.ResetCenter:
                _wheel.ResetCenter();
                break;
            case DeviceCommand.SetForceEnabled:
                Flags = arg != 0 ? Flags | DeviceFlags.ForceEnabled : Flags & ~DeviceFlags.ForceEnabled;
                break;
            case DeviceCommand.Reboot:
                return ConnectAsync();
            case DeviceCommand.SaveSettings:
            case DeviceCommand.EnterDfu:
            case DeviceCommand.Calibrate:
                break;
        }
        return Task.CompletedTask;
    }

    public void Step(double dt)
    {
        _wheel.Step(dt);
        StateReceived?.Invoke(this, BuildState());
    }

    private void ApplySettingsToWheel()
    {
        _wheel.MotionRangeDeg = _settings[SettingId.MotionRange].AsDouble;
        _wheel.SpringGain = _settings[SettingId.SpringStrength].AsDouble / 100.0;
        _wheel.DamperGain = Math.Max(_settings[SettingId.DamperStrength].AsDouble / 100.0, 0.01);
        _wheel.TotalStrength01 = _settings[SettingId.TotalStrength].AsDouble / 100.0;
    }

    private DeviceState BuildState() => new()
    {
        Firmware = FirmwareVersion,
        Flags = Flags,
        Position = _wheel.PositionNormalized,
        AngleDeciDeg = _wheel.AngleDeciDeg,
        Torque = _wheel.TorqueNormalized,
        MotorCurrentMa = (short)(_wheel.TorqueNormalized / 2),
        TemperatureC = 32,
        ErrorCode = 0,
    };
}
