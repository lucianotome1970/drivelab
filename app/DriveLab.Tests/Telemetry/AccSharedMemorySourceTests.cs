// ============================================================================
//  DriveLab
//  AccSharedMemorySourceTests.cs — Testes de degradação segura da fonte ACC (sem jogo / SO sem shared memory).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Telemetry.Sources;

namespace DriveLab.Tests.Telemetry;

public class AccSharedMemorySourceTests
{
    // O caminho de LEITURA precisa do ACC rodando no Windows (validação de rig). Aqui garantimos que, sem a
    // shared memory (Mac/CI ou jogo fechado), a fonte degrada em silêncio — nada de exceção vazando.
    [Fact]
    public void WhenUnavailable_DoesNotThrow_AndReportsNoData()
    {
        using var src = new AccSharedMemorySource();

        Assert.Equal("ACC/AC", src.Name);

        var read = src.TryRead(out var t);   // não pode lançar
        if (!src.IsAvailable)
        {
            Assert.False(read);
            Assert.False(t.HasData);
        }
    }
}
