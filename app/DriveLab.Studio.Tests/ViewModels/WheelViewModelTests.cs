// ============================================================================
//  DriveLab
//  WheelViewModelTests.cs — Testes de WheelViewModel (botões, cores, palhetas e persistência do perfil do volante).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DriveLab.Core.Protocol;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class WheelViewModelTests
{
    private static WheelViewModel New(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"wheelvm-{System.Guid.NewGuid():N}.json");
        return new WheelViewModel(new JsonWheelProfileStorage(path));
    }

    [Fact]
    public void Builds_Eight_Buttons_With_Default_Colors()
    {
        var vm = New(out _);
        Assert.Equal(8, vm.Buttons.Count);
        Assert.Equal("#BF5AF2", vm.Buttons.First(b => b.Name == "N").ColorHex);
        Assert.Equal("#34C759", vm.Buttons.First(b => b.Name == "DRS").ColorHex);
        Assert.Equal(4, vm.Paddles.Count);
    }

    [Fact]
    public void SetControlPressed_Lights_Named_Control_And_Paddle()
    {
        var vm = New(out _);
        vm.SetControlPressed("DRS", true);
        Assert.True(vm.Buttons.First(b => b.Name == "DRS").IsPressed);
        vm.SetControlPressed("DRS", false);
        Assert.False(vm.Buttons.First(b => b.Name == "DRS").IsPressed);

        vm.SetControlPressed("ShiftUp", true);
        Assert.True(vm.Paddles.First(p => p.Name == "ShiftUp").IsPressed);

        vm.SetControlPressed("does-not-exist", true); // no-op, não lança
    }

    [Fact]
    public void SelectButton_Marks_One_Selected()
    {
        var vm = New(out _);
        var pit = vm.Buttons.First(b => b.Name == "PIT");
        vm.SelectButtonCommand.Execute(pit);
        Assert.True(pit.IsSelected);
        Assert.Same(pit, vm.SelectedButton);
        Assert.Equal(1, vm.Buttons.Count(b => b.IsSelected));
    }

    [Fact]
    public void SetColor_Applies_To_Selected_Button()
    {
        var vm = New(out _);
        var n = vm.Buttons.First(b => b.Name == "N");
        vm.SelectButtonCommand.Execute(n);
        vm.SetColorCommand.Execute("#0A84FF");
        Assert.Equal("#0A84FF", n.ColorHex);
    }

    [Fact]
    public void SetColor_NoOp_When_Nothing_Selected()
    {
        var vm = New(out _);
        // Cor fora da paleta/defaults: se nada está selecionado, nenhum controle a recebe.
        vm.SetColorCommand.Execute("#123456");
        Assert.All(vm.Buttons, b => Assert.NotEqual("#123456", b.ColorHex));
    }

    [Fact]
    public void SetPaddleCount_Toggles_ShowBottomPair()
    {
        var vm = New(out _);
        vm.SetPaddleCountCommand.Execute("2");
        Assert.Equal(2, vm.PaddleCount);
        Assert.False(vm.ShowBottomPair);
        vm.SetPaddleCountCommand.Execute("4");
        Assert.True(vm.ShowBottomPair);
    }

    [Fact]
    public async Task Save_Then_Load_Restores_Colors_And_Paddles()
    {
        var vm = New(out var path);
        try
        {
            var n = vm.Buttons.First(b => b.Name == "N");
            vm.SelectButtonCommand.Execute(n);
            vm.SetColorCommand.Execute("#FFFFFF");
            vm.SetPaddleCountCommand.Execute("4");
            vm.BottomPair.SetFunctionCommand.Execute("Free");
            await vm.SaveCommand.ExecuteAsync(null);

            var vm2 = new WheelViewModel(new JsonWheelProfileStorage(path));
            await vm2.LoadCommand.ExecuteAsync(null);

            Assert.Equal("#FFFFFF", vm2.Buttons.First(b => b.Name == "N").ColorHex);
            Assert.Equal(PaddleFunction.Free, vm2.BottomPair.Function);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    // ---- integração ao vivo com o dispositivo (WheelDeviceSession) ----

    private static WheelViewModel WithSession(out FakeWheelTransport transport)
    {
        transport = new FakeWheelTransport();
        var session = new WheelDeviceSession(transport, new ImmediateUiDispatcher());
        var path = Path.Combine(Path.GetTempPath(), $"wheelvm-{System.Guid.NewGuid():N}.json");
        return new WheelViewModel(new JsonWheelProfileStorage(path), simulatorMode: false, session);
    }

    [Fact]
    public async Task Telemetry_Lights_Buttons_By_Bitmap()
    {
        var vm = WithSession(out var t);
        await vm.ConnectCommand.ExecuteAsync(null);

        t.RaiseState(new WheelState { Buttons = 0b101, ClutchLeft = new WheelAxis(0, 40000) }); // bits 0 e 2

        Assert.True(vm.Buttons[0].IsPressed);
        Assert.False(vm.Buttons[1].IsPressed);
        Assert.True(vm.Buttons[2].IsPressed);
        Assert.True(vm.ClutchLeft.IsPressed);   // eixo alto acende a embreagem esq.
        vm.Dispose();
    }

    [Fact]
    public async Task Connect_And_SetColor_Push_Leds_Live()
    {
        var vm = WithSession(out var t);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(t.LedSends >= 1);                 // conectar já empurra as cores atuais

        var before = t.LedSends;
        vm.SelectButtonCommand.Execute(vm.Buttons[0]);
        vm.SetColorCommand.Execute("#FF3B30");        // muda a cor → empurra ao vivo
        Assert.True(t.LedSends > before);
        Assert.Equal(new WheelLedColor(0xFF, 0x3B, 0x30), t.LastLed!.Leds[0]);
        vm.Dispose();
    }
}
