// ============================================================================
//  DriveLab
//  SimulatorWheelTransport.cs — Transporte de volante simulado (sem hardware): conecta e ecoa settings.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Simulator;

/// <summary>
/// <see cref="IWheelTransport"/> simulado para o modo <c>/simulator</c>. Conecta sob demanda
/// (botão Conectar) e ecoa settings em memória. NÃO faz streaming de telemetria: no simulador
/// os botões/pás são acionados pelo mouse (via WheelViewModel.SetControlPressed), então emitir
/// WheelState zeraria o "pressionado" a cada frame.
/// </summary>
public sealed class SimulatorWheelTransport : IWheelTransport
{
    private readonly object _sync = new();
    private readonly Dictionary<WheelSettingId, SettingValue> _settings = new();
    private readonly Random _rng = new();
    private Timer? _timer;
    private int _phase;

    // Estado do demo (sorteado a cada ~0,4 s, mantido entre sorteios).
    private uint _buttons;
    private int _activeKnob;
    private ushort _clutchL, _clutchR;
    private const int NewDrawEvery = 12;    // a ~30 Hz ≈ 0,4 s

    public bool IsConnected { get; private set; }
    public bool SupportsConfig => true;
    public FirmwareVersion FirmwareVersion { get; } = new(0, 26, 7, 14);

    public event EventHandler<WheelState>? StateReceived;

    public Task ConnectAsync(CancellationToken ct = default)
    {
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

    public Task WriteSettingAsync(WheelSettingId id, SettingValue value)
    {
        lock (_sync) { _settings[id] = value; }
        return Task.CompletedTask;
    }

    public Task<SettingValue> ReadSettingAsync(WheelSettingId id)
    {
        lock (_sync)
            return Task.FromResult(_settings.TryGetValue(id, out var v) ? v : new SettingValue(SettingType.UInt8, 0));
    }

    public Task SendCommandAsync(WheelCommandId command, byte arg = 0) => Task.CompletedTask;

    private WheelLedReport? _lastLed;
    public Task SendLedAsync(WheelLedReport led) { lock (_sync) _lastLed = led; return Task.CompletedTask; }

    // Ecoa as últimas cores enviadas (round-trip); vazio se nada foi enviado ainda.
    public Task<WheelLedReport> ReadLedsAsync()
    {
        lock (_sync)
            return Task.FromResult(_lastLed ?? new WheelLedReport(200, Array.Empty<WheelLedColor>()));
    }

    private void StartStreaming()
    {
        StopStreaming();
        _timer = new Timer(_ => Step(), null, 33, 33);   // ~30 Hz
    }

    private void StopStreaming()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>Demo auto-aleatório: sorteia botões/pás/embreagens/knob a cada ~0,4 s e mantém
    /// entre sorteios (streaming a ~30 Hz). É o que "aciona" o desenho no /simulator ao conectar.</summary>
    private void Step()
    {
        _phase++;
        if (_phase % NewDrawEvery == 0)
        {
            _buttons = 0;
            for (var b = 0; b < 8; b++)                        // 8 botões do desenho
                if (_rng.NextDouble() < 0.20) _buttons |= 1u << b;
            if (_rng.NextDouble() < 0.25) _buttons |= 1u << 10; // marcha ↓
            if (_rng.NextDouble() < 0.25) _buttons |= 1u << 11; // marcha ↑
            _clutchL = _rng.NextDouble() < 0.30 ? (ushort)50000 : (ushort)0;
            _clutchR = _rng.NextDouble() < 0.30 ? (ushort)50000 : (ushort)0;
            _activeKnob = _rng.Next(WheelState.EncoderCount);   // um knob girando
        }
        var state = new WheelState
        {
            Firmware = FirmwareVersion,
            Buttons = _buttons,
            ClutchLeft = new WheelAxis(0, _clutchL),
            ClutchRight = new WheelAxis(0, _clutchR),
        };
        state.EncoderDeltas[_activeKnob] = 1;                   // mantém o knob ativo aceso
        StateReceived?.Invoke(this, state);
    }
}
