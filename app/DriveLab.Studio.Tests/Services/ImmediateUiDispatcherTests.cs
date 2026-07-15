// ============================================================================
//  DriveLab
//  ImmediateUiDispatcherTests.cs — Testes de ImmediateUiDispatcher (execução síncrona do Post).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Xunit;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.Tests.Services;

public class ImmediateUiDispatcherTests
{
    [Fact]
    public void Post_Runs_Action_Synchronously()
    {
        var dispatcher = new ImmediateUiDispatcher();
        var ran = false;

        dispatcher.Post(() => ran = true);

        Assert.True(ran);
    }
}
