// ============================================================================
//  DriveLab
//  SettingFieldViewModelTests.cs — Testes de SettingFieldViewModel (leitura/escrita, presets, opções de enum).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;
using DriveLab.Studio.Localization;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

[Collection("Loc")]
public class SettingFieldViewModelTests
{
    private static SettingFieldViewModel New(out FakeTransport transport)
    {
        transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        return new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.MotionRange));
    }

    [Fact]
    public void Exposes_Descriptor_Metadata_And_Default()
    {
        var vm = New(out _);
        Assert.Equal(LocalizationManager.Get("Setting_MotionRange"), vm.DisplayName);
        Assert.Equal(90, vm.Min);
        Assert.Equal(2000, vm.Max);
        Assert.Equal(900, vm.Value);
    }

    [Fact]
    public async Task WriteAsync_Sends_Clamped_Value_To_Device()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        vm.Value = 750;
        await vm.WriteAsync();
        Assert.Equal(BaseSettingId.MotionRange, transport.LastWrite!.Value.id);
        Assert.Equal(750, transport.LastWrite!.Value.value.AsDouble);
    }

    [Fact]
    public async Task LoadAsync_Reads_Value_Without_Writing_Back()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        await vm.LoadAsync();
        Assert.Equal(900, vm.Value);         // FakeTransport.ReadSettingAsync returns 900
        Assert.Null(transport.LastWrite);    // load must not trigger a write
    }

    [Fact]
    public async Task WriteAsync_Does_Nothing_When_Disconnected()
    {
        var vm = New(out var transport); // transport NOT connected
        vm.Value = 750;
        await vm.WriteAsync();
        Assert.Null(transport.LastWrite);
    }

    [Fact]
    public async Task Value_Syncs_When_Same_Setting_Changed_Elsewhere()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        // recria vm sobre a mesma sessão para observar o evento
        var field = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.MotionRange));

        await session.WriteSettingAsync(BaseSettingId.MotionRange, new SettingValue(SettingType.UInt16, 720));

        Assert.Equal(720, field.Value);
    }

    [Fact]
    public void MotionRange_Exposes_Fixed_Presets()
    {
        var vm = New(out _);
        Assert.True(vm.HasPresets);
        Assert.Equal(new[] { 360, 540, 720, 900, 1080, 1440 }, vm.Presets);
    }

    [Fact]
    public void NonPreset_Setting_Has_No_Presets()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderCpr));
        Assert.False(vm.HasPresets);
        Assert.Empty(vm.Presets);
    }

    [Fact]
    public async Task Presets_Disabled_Until_Connected()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.MotionRange));

        Assert.False(vm.IsConnected);
        Assert.False(vm.SelectPresetCommand.CanExecute("900"));

        await session.ConnectAsync();
        Assert.True(vm.IsConnected);
        Assert.True(vm.SelectPresetCommand.CanExecute("900"));

        await session.DisconnectAsync();
        Assert.False(vm.IsConnected);
        Assert.False(vm.SelectPresetCommand.CanExecute("900"));
    }

    [Fact]
    public async Task SelectPreset_Sets_Value_And_Writes()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        vm.SelectPresetCommand.Execute("720");
        Assert.Equal(720, vm.Value);
        Assert.Equal(BaseSettingId.MotionRange, transport.LastWrite!.Value.id);
        Assert.Equal(720, transport.LastWrite!.Value.value.AsDouble);
    }

    [Fact]
    public void Integer_Setting_Is_Integer_And_Formats_Without_Decimals()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderCpr));

        Assert.True(vm.IsInteger);
        vm.Value = 12.81;
        Assert.Equal("13", vm.ValueText); // "0" format rounds to nearest integer
    }

    [Fact]
    public void Float_Setting_Is_Not_Integer()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.CurrentP));

        Assert.False(vm.IsInteger);
    }

    [Fact]
    public void EncoderType_Is_Enum_With_Two_Options()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderType));

        Assert.True(vm.HasOptions);
        Assert.Equal(2, vm.Options.Count);
        Assert.Equal(0, vm.Options[0].Value);
        Assert.Equal(1, vm.Options[1].Value);
        Assert.All(vm.Options, o => Assert.False(string.IsNullOrWhiteSpace(o.Label)));
    }

    [Fact]
    public async Task Selecting_Option_Sets_Value_And_Writes_When_Connected()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderType));

        await transport.ConnectAsync();
        vm.Options[1].SelectCommand.Execute(null);
        Assert.Equal(1, vm.Value);
        await vm.WriteAsync();
        Assert.Equal(BaseSettingId.EncoderType, transport.LastWrite!.Value.id);
        Assert.Equal(1, transport.LastWrite!.Value.value.AsDouble);
    }

    [Fact]
    public async Task Options_Disabled_Until_Connected()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderType));

        Assert.False(vm.IsConnected);
        Assert.False(vm.Options[0].SelectCommand.CanExecute(null));

        await session.ConnectAsync();
        Assert.True(vm.IsConnected);
        Assert.True(vm.Options[0].SelectCommand.CanExecute(null));

        await session.DisconnectAsync();
        Assert.False(vm.IsConnected);
        Assert.False(vm.Options[0].SelectCommand.CanExecute(null));
    }
}
