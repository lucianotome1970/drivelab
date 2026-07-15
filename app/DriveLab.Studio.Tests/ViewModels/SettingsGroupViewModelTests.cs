// ============================================================================
//  DriveLab
//  SettingsGroupViewModelTests.cs — Testes de SettingsGroupViewModel (grupo de campos de configuração).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

[Collection("Loc")]
public class SettingsGroupViewModelTests
{
    private static readonly BaseSettingId[] Ids =
    {
        BaseSettingId.TotalStrength, BaseSettingId.MaxTorqueLimit, BaseSettingId.DamperStrength,
    };

    private static SettingsGroupViewModel New(out FakeTransport transport)
    {
        transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        return new SettingsGroupViewModel(session, "Base do Volante", Ids);
    }

    [Fact]
    public void Exposes_Title_And_A_Field_Per_Id()
    {
        var vm = New(out _);
        Assert.Equal("Base do Volante", vm.Title);
        Assert.Equal(3, vm.Fields.Count);
        Assert.Equal(DriveLab.Studio.Localization.LocalizationManager.Get("Setting_TotalStrength"), vm.Fields[0].DisplayName);
    }

    [Fact]
    public async Task LoadAsync_Populates_Field_Values_From_Device()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        await vm.LoadAsync();
        // FakeTransport.ReadSettingAsync returns 900 for every field
        Assert.All(vm.Fields, f => Assert.Equal(900, f.Value));
    }

    [Fact]
    public async Task Fields_AutoLoad_When_Session_Connects()
    {
        var transport = new FakeTransport();
        var session = new DeviceSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingsGroupViewModel(session, "Base do Volante", Ids);

        await session.ConnectAsync(); // raises Connected -> auto-load

        Assert.All(vm.Fields, f => Assert.Equal(900, f.Value));
    }
}
