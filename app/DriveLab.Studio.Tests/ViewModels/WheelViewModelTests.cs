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
    public void LedBrightnessPercent_Maps_To_Byte_And_Back()
    {
        var vm = New(out _);
        vm.LedBrightnessPercent = 100;
        Assert.Equal((byte)255, vm.LedBrightness);
        vm.LedBrightnessPercent = 0;
        Assert.Equal((byte)0, vm.LedBrightness);
        vm.LedBrightness = 128;
        Assert.Equal(50, vm.LedBrightnessPercent);
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

    [Fact]
    public async Task Telemetry_Turns_Knob_On_And_It_Decays()
    {
        var vm = WithSession(out var t);
        await vm.ConnectCommand.ExecuteAsync(null);

        // gira o encoder 0 → knob 0 (BRAKE BIAS) acende; bit 10 = marcha ↓ pressiona
        t.RaiseState(new WheelState { EncoderDeltas = new sbyte[] { 4, 0, 0, 0, 0 }, Buttons = 1u << 10 });
        Assert.True(vm.Knobs[0].Glow > 0.9);
        Assert.True(vm.ShiftDown.IsPressed);

        // frame sem giro → o brilho decai (não fica travado aceso)
        t.RaiseState(new WheelState { EncoderDeltas = new sbyte[5] });
        Assert.InRange(vm.Knobs[0].Glow, 0.01, 0.99);
        vm.Dispose();
    }

    [Fact]
    public async Task Combined_Clutch_One_Press_Lights_Both()
    {
        var vm = WithSession(out var t);
        await vm.ConnectCommand.ExecuteAsync(null);
        // default: Function=Clutch, Mode=Combined
        t.RaiseState(new WheelState { ClutchLeft = new WheelAxis(0, 50000), ClutchRight = new WheelAxis(0, 0) });
        Assert.True(vm.ClutchLeft.IsPressed);
        Assert.True(vm.ClutchRight.IsPressed);   // combinado: 1 acende as 2

        vm.BottomPair.Mode = PaddleMode.Independent;
        t.RaiseState(new WheelState { ClutchLeft = new WheelAxis(0, 50000), ClutchRight = new WheelAxis(0, 0) });
        Assert.True(vm.ClutchLeft.IsPressed);
        Assert.False(vm.ClutchRight.IsPressed);  // independente: só a esquerda
        vm.Dispose();
    }

    [Fact]
    public async Task Reconnect_Reads_Firmware_Settings_From_Device_And_Discards_Unsaved()
    {
        var vm = WithSession(out _);
        await vm.ConnectCommand.ExecuteAsync(null);

        // Config gravada na placa (o firmware guarda esse subconjunto):
        vm.BottomPair.BitePoint = 30;
        vm.BottomPair.Mode = PaddleMode.Independent;

        await vm.DisconnectCommand.ExecuteAsync(null);

        // Edição local NÃO salva (não vai à placa enquanto desconectado):
        vm.BottomPair.BitePoint = 80;
        vm.BottomPair.Mode = PaddleMode.Combined;

        // Reconectou → lê da placa, descartando o não salvo:
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(30, vm.BottomPair.BitePoint);
        Assert.Equal(PaddleMode.Independent, vm.BottomPair.Mode);
        Assert.False(vm.IsDirty);
        vm.Dispose();
    }

    [Fact]
    public async Task Reconnect_Reads_Button_Colors_From_Device()
    {
        var vm = WithSession(out _);
        await vm.ConnectCommand.ExecuteAsync(null);

        // Muda a cor do 1º botão e "salva no controlador" → cores vão à placa (fake ecoa no ReadLeds):
        vm.SelectButtonCommand.Execute(vm.Buttons[0]);
        vm.SetColorCommand.Execute("#0A84FF");
        await vm.SaveToControllerCommand.ExecuteAsync(null);

        // Desconecta e faz uma edição local NÃO salva (não chega à placa enquanto desconectado):
        await vm.DisconnectCommand.ExecuteAsync(null);
        vm.SetColorCommand.Execute("#FF3B30");

        // Reconecta → deve ler a cor salva na placa (#0A84FF), descartando o não salvo:
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal("#0A84FF", vm.Buttons[0].ColorHex);
        vm.Dispose();
    }

    [Fact]
    public async Task SaveToController_Enabled_Only_When_Dirty_And_Connected()
    {
        var vm = WithSession(out _);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveToControllerCommand.CanExecute(null));  // nada alterado

        vm.SelectButtonCommand.Execute(vm.Buttons[0]);
        vm.SetColorCommand.Execute("#0A84FF");                      // alterou → dirty
        Assert.True(vm.IsDirty);
        Assert.True(vm.SaveToControllerCommand.CanExecute(null));

        await vm.SaveToControllerCommand.ExecuteAsync(null);        // salva → limpa dirty
        Assert.False(vm.IsDirty);
        Assert.False(vm.SaveToControllerCommand.CanExecute(null));
        vm.Dispose();
    }
}
