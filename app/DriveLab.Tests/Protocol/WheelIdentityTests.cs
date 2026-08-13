// ============================================================================
//  DriveLab
//  WheelIdentityTests.cs — Testes dos valores fixos da identidade USB do rim.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Tests.Protocol;

public class WheelIdentityTests
{
    [Fact]
    public void Identity_Has_Expected_Vid_Pid()
    {
        Assert.Equal(0x1209, WheelDeviceIdentity.VendorId);
        Assert.Equal(0x0004, WheelDeviceIdentity.ProductId);
        Assert.Equal(1, WheelDeviceIdentity.ProtocolVersion);
    }
}
