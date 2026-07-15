// ============================================================================
//  DriveLab
//  BaseViewModelTests.cs — Testes de BaseViewModel (força total: leitura, escrita e sincronização).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Xunit;
using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Tests.ViewModels;

public class BaseViewModelTests
{
    private static BaseViewModel New(out FakeTransport transport)
    {
        transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        return new BaseViewModel(session);
    }

    [Fact]
    public async Task TotalStrength_Writes_Setting_When_Connected()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();

        vm.TotalStrength = 60;

        Assert.Equal(SettingId.TotalStrength, transport.LastWrite!.Value.id);
        Assert.Equal(60, transport.LastWrite!.Value.value.AsDouble);
        Assert.Equal(SettingType.UInt8, transport.LastWrite!.Value.value.Type);
    }

    [Fact]
    public void TotalStrength_Does_Nothing_When_Disconnected()
    {
        var vm = New(out var transport); // not connected

        vm.TotalStrength = 60;

        Assert.Null(transport.LastWrite);
    }

    [Fact]
    public async Task TotalStrength_Loads_From_Device_On_Connect()
    {
        var transport = new FakeTransport(); // ReadSettingAsync returns 900 (generic fake)
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        var vm = new BaseViewModel(session);

        await session.ConnectAsync();

        Assert.Equal(900, vm.TotalStrength);       // read happened
        Assert.Null(transport.LastWrite);          // load must not echo a write back
    }

    [Fact]
    public async Task TotalStrength_Syncs_When_Setting_Changed_Elsewhere()
    {
        var transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        var vm = new BaseViewModel(session);

        // Outra tela (ex.: Base do Volante) grava o TotalStrength no dispositivo.
        await session.WriteSettingAsync(SettingId.TotalStrength, new SettingValue(SettingType.UInt8, 40));

        Assert.Equal(40, vm.TotalStrength);
    }
}
