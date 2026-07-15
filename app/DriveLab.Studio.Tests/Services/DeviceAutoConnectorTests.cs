// ============================================================================
//  DriveLab
//  DeviceAutoConnectorTests.cs — Testes de DeviceAutoConnector (conexão/desconexão automática por presença).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using Xunit;

namespace DriveLab.Studio.Tests.Services;

public class DeviceAutoConnectorTests
{
    [Fact]
    public async Task Connects_When_Device_Present_Disconnects_When_Gone()
    {
        var transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        var present = false;
        using var ac = new DeviceAutoConnector(
            () => session.IsConnected, session.ConnectAsync, session.DisconnectAsync,
            () => present, new ImmediateUiDispatcher());

        await ac.PollOnceAsync();
        Assert.False(session.IsConnected);   // ausente → não conecta

        present = true;
        await ac.PollOnceAsync();
        Assert.True(session.IsConnected);    // apareceu → conecta

        await ac.PollOnceAsync();
        Assert.True(session.IsConnected);    // continua presente → segue conectado (1 connect só)
        Assert.Equal(1, transport.ConnectCalls);

        present = false;
        await ac.PollOnceAsync();
        Assert.False(session.IsConnected);   // sumiu → desconecta
    }

    [Fact]
    public async Task Probe_Throwing_Is_Treated_As_Absent()
    {
        var transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        using var ac = new DeviceAutoConnector(
            () => session.IsConnected, session.ConnectAsync, session.DisconnectAsync,
            () => throw new System.Exception("HID falhou"), new ImmediateUiDispatcher());

        await ac.PollOnceAsync(); // não lança; trata como ausente
        Assert.False(session.IsConnected);
    }
}
