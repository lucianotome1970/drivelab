// ============================================================================
//  DriveLab
//  SimulatorPedalTransport.cs — Transporte simulado de pedaleira: gera telemetria sintética (clutch/brake/throttle) sem hardware.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Threading;
using DriveLab.Core.Pedals;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Simulator;

public sealed class SimulatorPedalTransport : IPedalTransport
{
    private readonly object _sync = new();
    private readonly PedalDeviceModel _model = new();
    private Timer? _timer;
    private int _periodMs;
    private volatile bool _streaming;
    private int _phase;

    public bool IsConnected { get; private set; }
    public bool SupportsConfig => true;
    public FirmwareVersion FirmwareVersion { get; } = new(0, 26, 7, 13);
    public byte Flags { get; private set; }

    public event EventHandler<PedalState>? StateReceived;

    public Task ConnectAsync(CancellationToken ct = default)
    {
        lock (_sync) { _model.SeedDefaults(); }
        IsConnected = true;
        StartStreaming();
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
        lock (_sync) { _model.WriteSetting(id, pedal, value); }
        return Task.CompletedTask;
    }

    public Task<SettingValue> ReadSettingAsync(PedalSettingId id, PedalIndex pedal)
    {
        lock (_sync) { return Task.FromResult(_model.ReadSetting(id, pedal)); }
    }

    public Task SendCommandAsync(PedalCommandId command, byte arg = 0)
    {
        lock (_sync)
        {
            switch (command)
            {
                case PedalCommandId.LoadDefaults: _model.LoadDefaults(); break;
                case PedalCommandId.CalibrateStart: if (arg < 3) _model.CalibrateStart((PedalIndex)arg); break;
                case PedalCommandId.CalibrateStop: if (arg < 3) _model.CalibrateStop((PedalIndex)arg); break;
                case PedalCommandId.SaveToFlash: break;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Test/demo hook: injeta leituras cruas (use com o streaming parado).</summary>
    public void SetRawInputs(ushort clutch, ushort brake, ushort throttle)
    {
        lock (_sync) { _model.SetRawInputs(clutch, brake, throttle); }
    }

    public void Step()
    {
        PedalState state;
        lock (_sync)
        {
            if (_streaming)
            {
                _phase++;
                _model.SetRawInputs(
                    GenerateRaw(_phase), GenerateRaw(_phase + 40), GenerateRaw(_phase + 80));
            }
            state = _model.BuildState(FirmwareVersion, Flags);
        }
        StateReceived?.Invoke(this, state);
    }

    public void StartStreaming(int hz = 60)
    {
        StopStreaming();
        _periodMs = Math.Max(1, 1000 / hz);
        _streaming = true;
        _timer = new Timer(_ => { if (_streaming) Step(); }, null, _periodMs, _periodMs);
    }

    public void StopStreaming()
    {
        _streaming = false;
        _timer?.Dispose();
        _timer = null;
    }

    private static ushort GenerateRaw(int phase)
    {
        var t = (phase % 120) / 120.0;          // sawtooth 0..1
        var tri = t < 0.5 ? t * 2 : (1 - t) * 2; // triângulo 0..1..0
        return (ushort)Math.Round(tri * 4095);
    }
}
