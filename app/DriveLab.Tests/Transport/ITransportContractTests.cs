// ============================================================================
//  DriveLab
//  ITransportContractTests.cs — Testes do contrato ITransport e valores de DeviceCommand.
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
        Assert.Equal(1, (byte)DeviceCommand.Reboot);
        Assert.Equal(3, (byte)DeviceCommand.ResetCenter);
        Assert.Equal(6, (byte)DeviceCommand.SetForceEnabled);
    }

    [Fact]
    public void ITransport_Is_An_Interface()
    {
        Assert.True(typeof(ITransport).IsInterface);
    }
}
