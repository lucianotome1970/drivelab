// ============================================================================
//  DriveLab
//  ITransportContractTests.cs — Testes do contrato IBaseTransport e valores de BaseCommand.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Transport;

namespace DriveLab.Tests.Transport;

public class ITransportContractTests
{
    [Fact]
    public void DeviceCommand_Values_Match_Protocol()
    {
        Assert.Equal(1, (byte)BaseCommand.Reboot);
        Assert.Equal(3, (byte)BaseCommand.ResetCenter);
        Assert.Equal(6, (byte)BaseCommand.SetForceEnabled);
    }

    [Fact]
    public void ITransport_Is_An_Interface()
    {
        Assert.True(typeof(IBaseTransport).IsInterface);
    }
}
