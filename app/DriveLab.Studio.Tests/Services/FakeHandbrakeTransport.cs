// ============================================================================
//  DriveLab
//  FakeHandbrakeTransport.cs — Transporte de freio de mão em memória para testes de VM.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;

namespace DriveLab.Studio.Tests.Services;

/// <summary>Fake de <see cref="IHandbrakeTransport"/>: guarda settings em memória e
/// devolve os defaults do schema na leitura, para exercitar o VM sem hardware.</summary>
public sealed class FakeHandbrakeTransport : IHandbrakeTransport
{
    private readonly Dictionary<HandbrakeSettingId, SettingValue> _store = new();

    public bool IsConnected { get; private set; }
    public FirmwareVersion FirmwareVersion => new(1, 0, 0, 0);
    public bool SupportsConfig => true;

    /// <summary>Quando true, ReadSettingAsync estoura (simula 0x16 perdido / timeout do firmware).</summary>
    public bool ThrowOnRead { get; set; }

    public event EventHandler<PedalState>? StateReceived;

    public Task ConnectAsync(CancellationToken ct = default) { IsConnected = true; return Task.CompletedTask; }
    public Task DisconnectAsync() { IsConnected = false; return Task.CompletedTask; }

    public Task WriteSettingAsync(HandbrakeSettingId id, SettingValue value)
    {
        _store[id] = value;
        return Task.CompletedTask;
    }

    public Task<SettingValue> ReadSettingAsync(HandbrakeSettingId id)
    {
        if (ThrowOnRead)
            throw new TimeoutException($"Sem SettingValue p/ field {(byte)id} (fake)");
        if (_store.TryGetValue(id, out var v))
            return Task.FromResult(v);
        var d = HandbrakeSettingsSchema.Get(id);
        return Task.FromResult(new SettingValue(d.Type, d.Default));
    }

    public Task SendCommandAsync(PedalCommandId command, byte arg = 0) => Task.CompletedTask;

    public void RaiseState(PedalState state) => StateReceived?.Invoke(this, state);
}
