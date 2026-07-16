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

    public bool IsConnected { get; private set; }
    public bool SupportsConfig => true;
    public FirmwareVersion FirmwareVersion { get; } = new(0, 26, 7, 14);

    // Nunca disparado (sem streaming); presente para satisfazer o contrato.
#pragma warning disable CS0067
    public event EventHandler<WheelState>? StateReceived;
#pragma warning restore CS0067

    public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }
    public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }

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
    public Task SendLedAsync(WheelLedReport led) => Task.CompletedTask;
}
