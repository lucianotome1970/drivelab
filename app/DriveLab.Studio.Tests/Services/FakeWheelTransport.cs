// ============================================================================
//  DriveLab
//  FakeWheelTransport.cs — Transporte de volante em memória para testes de VM (telemetria + LED).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Studio.Tests.Services;

/// <summary>Fake de <see cref="IWheelTransport"/>: conecta em memória, registra os LEDs enviados
/// e permite injetar telemetria (<see cref="RaiseState"/>) para exercitar o VM sem hardware.</summary>
public sealed class FakeWheelTransport : IWheelTransport
{
    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion => new(1, 0, 0, 0);
    public bool SupportsConfig => true;

    public int LedSends { get; private set; }
    public WheelLedReport? LastLed { get; private set; }

    public event EventHandler<WheelState>? StateReceived;

    private readonly Dictionary<WheelSettingId, SettingValue> _settings = new();

    public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }
    public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }

    // Ecoa as settings escritas (guarda em memória) → permite testar o round-trip write→read da placa.
    public Task WriteSettingAsync(WheelSettingId id, SettingValue value) { _settings[id] = value; return Task.CompletedTask; }
    public Task<SettingValue> ReadSettingAsync(WheelSettingId id) =>
        Task.FromResult(_settings.TryGetValue(id, out var v) ? v : new SettingValue(SettingType.UInt8, 0));
    public Task SendCommandAsync(WheelCommandId command, byte arg = 0) => Task.CompletedTask;

    public Task SendLedAsync(WheelLedReport led)
    {
        LedSends++;
        LastLed = led;
        return Task.CompletedTask;
    }

    // Ecoa as últimas cores enviadas (round-trip) — permite testar a leitura das cores da placa.
    public Task<WheelLedReport> ReadLedsAsync() =>
        Task.FromResult(LastLed ?? new WheelLedReport(200, System.Array.Empty<WheelLedColor>()));

    public void RaiseState(WheelState state) => StateReceived?.Invoke(this, state);
}
