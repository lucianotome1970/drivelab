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

    /// <summary>Todos os controles enviados, na ordem. O <see cref="LastControl"/> sozinho não serve
    /// para verificar a força DURANTE um teste: o último envio é sempre a parada em zero, então
    /// asserção sobre ele passaria mesmo que nada tivesse sido aplicado no meio.</summary>
    public List<BaseDirectControl> Controls { get; } = new();
    public (BaseCommand cmd, byte arg)? LastCommand { get; private set; }
    public (BaseSettingId id, SettingValue value)? LastWrite { get; private set; }

    public Task ConnectAsync(CancellationToken ct = default) { ConnectCalls++; IsConnected = ConnectSucceeds; return Task.CompletedTask; }
    public Task DisconnectAsync() { DisconnectCalls++; IsConnected = false; return Task.CompletedTask; }
    public Task WriteSettingAsync(BaseSettingId id, SettingValue value) { LastWrite = (id, value); return Task.CompletedTask; }
    /// <summary>Valores por setting, para quem precisa de um em particular. O que não estiver aqui
    /// cai no 900 de sempre — havia um só valor para toda pergunta, e isso deixou de servir quando o
    /// painel de testes passou a ler o AJUSTE DE FORÇA para descontá-lo: força total e limite máximo
    /// valendo 900% cada davam um fator de 81, e o teste media uma base que não existe.</summary>
    public Dictionary<BaseSettingId, SettingValue> Settings { get; } = new();

    public Task<SettingValue> ReadSettingAsync(BaseSettingId id) =>
        Task.FromResult(Settings.TryGetValue(id, out var v) ? v : new SettingValue(SettingType.UInt16, 900));
    /// <summary>Padrao de fabrica devolvido pela base. Diferente do valor atual de proposito:
    /// e assim que o teste distingue "perguntou o padrao" de "releu o valor gravado".</summary>
    public SettingValue DefaultToReturn { get; set; } = new(SettingType.UInt16, 540);
    public BaseSettingId? LastDefaultAsked { get; private set; }
    public Task<SettingValue> ReadSettingDefaultAsync(BaseSettingId id)
    {
        LastDefaultAsked = id;
        return Task.FromResult(DefaultToReturn);
    }

    public Task SendDirectControlAsync(BaseDirectControl control)
    {
        LastControl = control;
        Controls.Add(control);
        return Task.CompletedTask;
    }
    public Task SendCommandAsync(BaseCommand command, byte arg = 0) { LastCommand = (command, arg); return Task.CompletedTask; }

    /// <summary>Test hook: simulate a telemetry report arriving from the device.</summary>
    public void Emit(BaseState state) => StateReceived?.Invoke(this, state);
}
