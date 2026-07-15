// ============================================================================
//  DriveLab
//  DeviceSessionConnectedTests.cs — Testes do evento Connected de DeviceSession.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using Xunit;

namespace DriveLab.Studio.Tests.Services;

public class DeviceSessionConnectedTests
{
    [Fact]
    public async Task ConnectAsync_Raises_Connected()
    {
        var transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        var raised = false;
        session.Connected += (_, _) => raised = true;

        await session.ConnectAsync();

        Assert.True(raised);
    }
}
