// ============================================================================
//  DriveLab
//  SimulatorHandbrakeTransport.cs — Transporte simulado de freio de mão: gera telemetria sintética sem hardware.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Threading;
using DriveLab.Core.Handbrake;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Simulator;

public sealed class SimulatorHandbrakeTransport : IHandbrakeTransport
{
    private readonly object _sync = new();
    private readonly HandbrakeDeviceModel _model = new();
    private Timer? _timer;
    private int _periodMs;
    private volatile bool _streaming;
    private int _phase;

    public bool IsConnected { get; private set; }
    public bool SupportsConfig => true;
    public FirmwareVersion FirmwareVersion { get; } = new(0, 26, 7, 14);
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

    public Task WriteSettingAsync(HandbrakeSettingId id, SettingValue value)
    {
        lock (_sync) { _model.WriteSetting(id, value); }
        return Task.CompletedTask;
    }

    public Task<SettingValue> ReadSettingAsync(HandbrakeSettingId id)
    {
        lock (_sync) { return Task.FromResult(_model.ReadSetting(id)); }
    }

    public Task SendCommandAsync(PedalCommandId command, byte arg = 0)
    {
        lock (_sync)
        {
            switch (command)
            {
                case PedalCommandId.LoadDefaults: _model.LoadDefaults(); break;
                case PedalCommandId.CalibrateStart: _model.CalibrateStart(); break;
                case PedalCommandId.CalibrateStop: _model.CalibrateStop(); break;
                case PedalCommandId.SaveToFlash: break;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Test/demo hook: injeta leitura crua (use com o streaming parado).</summary>
    public void SetRawInput(ushort raw)
    {
        lock (_sync) { _model.SetRawInput(raw); }
    }

    public void Step()
    {
        PedalState state;
        lock (_sync)
        {
            if (_streaming)
            {
                _phase++;
                _model.SetRawInput(GenerateRaw(_phase));
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
        var t = (phase % 120) / 120.0;           // sawtooth 0..1
        var tri = t < 0.5 ? t * 2 : (1 - t) * 2;  // triângulo 0..1..0
        return (ushort)Math.Round(tri * 4095);
    }
}
