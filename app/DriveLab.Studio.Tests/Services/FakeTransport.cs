// ============================================================================
//  DriveLab
//  FakeTransport.cs — Transporte falso (IBaseTransport) controlável para testes determinísticos de BaseSession.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Studio.Tests.Services;

/// <summary>Controllable IBaseTransport for deterministic BaseSession tests (no timer).</summary>
public sealed class FakeTransport : IBaseTransport
{
    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion { get; } = new(0, 1, 0, 0);
    public event EventHandler<BaseState>? StateReceived;

    public int ConnectCalls { get; private set; }
    public int DisconnectCalls { get; private set; }

    /// <summary>Quando false, simula hardware ausente: ConnectAsync não conecta.</summary>
    public bool ConnectSucceeds { get; set; } = true;
    public BaseDirectControl? LastControl { get; private set; }
    public (BaseCommand cmd, byte arg)? LastCommand { get; private set; }
    public (BaseSettingId id, SettingValue value)? LastWrite { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default) { ConnectCalls++; IsConnected = ConnectSucceeds; return Task.CompletedTask; }
    public Task DisconnectAsync() { DisconnectCalls++; IsConnected = false; return Task.CompletedTask; }
    public Task WriteSettingAsync(BaseSettingId id, SettingValue value) { LastWrite = (id, value); return Task.CompletedTask; }
    public Task<SettingValue> ReadSettingAsync(BaseSettingId id) => Task.FromResult(new SettingValue(SettingType.UInt16, 900));
    public Task SendDirectControlAsync(BaseDirectControl control) { LastControl = control; return Task.CompletedTask; }
    public Task SendCommandAsync(BaseCommand command, byte arg = 0) { LastCommand = (command, arg); return Task.CompletedTask; }

    /// <summary>Test hook: simulate a telemetry report arriving from the device.</summary>
    public void Emit(BaseState state) => StateReceived?.Invoke(this, state);
}
