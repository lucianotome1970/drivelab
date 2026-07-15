// ============================================================================
//  DriveLab
//  DeviceSessionDisposeTests.cs — Testes de Dispose de DeviceSession (interrompe eventos de estado).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;
using DriveLab.Studio.Services;
using Xunit;

namespace DriveLab.Studio.Tests.Services;

public class DeviceSessionDisposeTests
{
    [Fact]
    public void Dispose_Stops_ReRaising_State()
    {
        var transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        var count = 0;
        session.StateReceived += (_, _) => count++;

        transport.Emit(new BaseState());
        Assert.Equal(1, count);

        session.Dispose();
        transport.Emit(new BaseState());
        Assert.Equal(1, count); // no further events after dispose
    }
}
