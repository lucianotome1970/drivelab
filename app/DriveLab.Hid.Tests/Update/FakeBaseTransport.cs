// ============================================================================
//  DriveLab
//  FakeBaseTransport.cs — IBaseTransport falso e mínimo pra testar BaseUpdater
//  sem tocar HID/hardware real.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Hid.Tests.Update;

public sealed class FakeBaseTransport : IBaseTransport
{
    public bool IsConnected { get; private set; } = true;
    public FirmwareVersion FirmwareVersion { get; } = new(0, 1, 0, 0);
    public event EventHandler<BaseState>? StateReceived;

    public (BaseCommand cmd, byte arg)? LastCommand { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }
    public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }
    public Task WriteSettingAsync(BaseSettingId id, SettingValue value) => Task.CompletedTask;
    public Task<SettingValue> ReadSettingAsync(BaseSettingId id) => Task.FromResult(new SettingValue(SettingType.UInt16, 0));
    public Task SendDirectControlAsync(BaseDirectControl control) => Task.CompletedTask;
    public Task SendCommandAsync(BaseCommand command, byte arg = 0) { LastCommand = (command, arg); return Task.CompletedTask; }

    public void Emit(BaseState state) => StateReceived?.Invoke(this, state);
}
