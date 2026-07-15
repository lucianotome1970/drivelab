// ============================================================================
//  DriveLab
//  FakeTransport.cs — Transporte falso (ITransport) controlável para testes determinísticos de DeviceSession.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Studio.Tests.Services;

/// <summary>Controllable ITransport for deterministic DeviceSession tests (no timer).</summary>
public sealed class FakeTransport : ITransport
{
    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion { get; } = new(0, 1, 0, 0);
    public event EventHandler<DeviceState>? StateReceived;

    public int ConnectCalls { get; private set; }
    public int DisconnectCalls { get; private set; }

    /// <summary>Quando false, simula hardware ausente: ConnectAsync não conecta.</summary>
    public bool ConnectSucceeds { get; set; } = true;
    public DirectControl? LastControl { get; private set; }
    public (DeviceCommand cmd, byte arg)? LastCommand { get; private set; }
    public (SettingId id, SettingValue value)? LastWrite { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default) { ConnectCalls++; IsConnected = ConnectSucceeds; return Task.CompletedTask; }
    public Task DisconnectAsync() { DisconnectCalls++; IsConnected = false; return Task.CompletedTask; }
    public Task WriteSettingAsync(SettingId id, SettingValue value) { LastWrite = (id, value); return Task.CompletedTask; }
    public Task<SettingValue> ReadSettingAsync(SettingId id) => Task.FromResult(new SettingValue(SettingType.UInt16, 900));
    public Task SendDirectControlAsync(DirectControl control) { LastControl = control; return Task.CompletedTask; }
    public Task SendCommandAsync(DeviceCommand command, byte arg = 0) { LastCommand = (command, arg); return Task.CompletedTask; }

    /// <summary>Test hook: simulate a telemetry report arriving from the device.</summary>
    public void Emit(DeviceState state) => StateReceived?.Invoke(this, state);
}
