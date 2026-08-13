// ============================================================================
//  DriveLab
//  HostDeviceAccessCoordinatorTests.cs — Testes do coordenador de acesso
//  exclusivo à USB durante um update: BeginExclusive pausa o auto-connect da
//  base e solta o handle (disconnect); EndExclusive retoma; kinds != Base são
//  no-op; e mesmo com o disconnect lançando, o auto-connect fica pausado.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Update;
using DriveLab.Studio.Services;
using Xunit;

namespace DriveLab.Studio.Tests.Services;

public class HostDeviceAccessCoordinatorTests
{
    private static (HostDeviceAccessCoordinator coord, DeviceAutoConnector ac, FakeTransport transport, BaseSession session) Build()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var present = true;
        var ac = new DeviceAutoConnector(
            () => session.IsConnected, session.ConnectAsync, session.DisconnectAsync,
            () => present, new ImmediateUiDispatcher());
        var coord = new HostDeviceAccessCoordinator(ac, session.DisconnectAsync);
        return (coord, ac, transport, session);
    }

    [Fact]
    public async Task Begin_Pauses_AutoConnect_And_Releases_Handle()
    {
        var (coord, ac, transport, session) = Build();
        await session.ConnectAsync();
        Assert.True(session.IsConnected);

        await coord.BeginExclusiveAsync(DeviceKind.Base);

        // Handle solto…
        Assert.False(session.IsConnected);
        Assert.Equal(1, transport.DisconnectCalls);

        // …e o auto-connect não reabre o device enquanto pausado, mesmo estando "presente".
        // ConnectCalls fica em 1 (só o connect explícito do setup) — o poll pausado não reconecta.
        await ac.PollOnceAsync();
        Assert.False(session.IsConnected);
        Assert.Equal(1, transport.ConnectCalls);
    }

    [Fact]
    public async Task End_Resumes_AutoConnect_Which_Reconnects()
    {
        var (coord, ac, transport, session) = Build();
        await coord.BeginExclusiveAsync(DeviceKind.Base);

        await coord.EndExclusiveAsync(DeviceKind.Base);

        await ac.PollOnceAsync();          // retomado → reconecta a base (firmware novo já rodando)
        Assert.True(session.IsConnected);
        Assert.Equal(1, transport.ConnectCalls);
    }

    [Fact]
    public async Task NonBase_Kind_Is_NoOp()
    {
        var (coord, _, transport, session) = Build();
        await session.ConnectAsync();

        await coord.BeginExclusiveAsync(DeviceKind.Pedal);

        Assert.True(session.IsConnected);        // não mexeu na base
        Assert.Equal(0, transport.DisconnectCalls);
    }
}
