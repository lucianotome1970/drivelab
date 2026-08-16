// ============================================================================
//  DriveLab
//  DashboardViewModelTests.cs — Testes de DashboardViewModel (telemetria, centralização, faixa de movimento).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Xunit;
using DriveLab.Core.Protocol;
using DriveLab.Core.Settings;
using DriveLab.Core.Transport;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Tests.ViewModels;

public class DashboardViewModelTests
{
    private static DashboardViewModel New(out FakeTransport transport)
    {
        transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        return new DashboardViewModel(session);
    }

    [Fact]
    public void Telemetry_Updates_AngleDegrees_And_PositionPercent()
    {
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = 2700, Position = 5000 });

        Assert.Equal(270.0, vm.AngleDegrees, precision: 3);   // 2700 deci-deg = 270°
        Assert.Equal(50.0, vm.PositionPercent, precision: 3);  // 5000/10000 = 50%
    }

    [Fact]
    public async Task CenterCommand_Sends_ResetCenter()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        await vm.CenterCommand.ExecuteAsync(null);
        Assert.Equal(BaseCommand.ResetCenter, transport.LastCommand!.Value.cmd);
    }

    [Fact]
    public async Task SetMaxAngle_Does_Not_Write_Until_Saved()
    {
        // A base é a fonte de verdade: escolher o ângulo NÃO reconfigura a placa —
        // fica só na tela até o "Salvar no controlador".
        var vm = New(out var transport);
        await transport.ConnectAsync();
        vm.SetMaxAngleCommand.Execute("900");

        Assert.Equal(900, vm.MotionRange);   // a tela reflete a escolha
        Assert.Null(transport.LastWrite);    // ...mas nada foi para a placa
        Assert.True(vm.IsDirty);             // e o Salvar acende
    }

    [Fact]
    public async Task Save_Sends_Pending_MotionRange_Then_Persists()
    {
        var vm = New(out var transport);
        await transport.ConnectAsync();
        vm.SetMaxAngleCommand.Execute("900");

        await vm.SaveToControllerCommand.ExecuteAsync(null);

        Assert.Equal(BaseSettingId.MotionRange, transport.LastWrite!.Value.id);
        Assert.Equal(900, transport.LastWrite!.Value.value.AsDouble);
        Assert.Equal(BaseCommand.SaveSettings, transport.LastCommand!.Value.cmd);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task Center_Does_Nothing_When_Disconnected()
    {
        var vm = New(out var transport); // not connected
        await vm.CenterCommand.ExecuteAsync(null);
        Assert.Null(transport.LastCommand);
    }

    [Fact]
    public async Task MotionRange_Syncs_When_Setting_Changed_Elsewhere()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new DashboardViewModel(session);

        // Outra tela (ex.: Ajustes) grava o MotionRange no dispositivo.
        await session.WriteSettingAsync(BaseSettingId.MotionRange, new SettingValue(SettingType.UInt16, 540));

        Assert.Equal(540, vm.MotionRange);
    }

    [Fact]
    public async Task Presets_And_Center_Disabled_Until_Connected()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new DashboardViewModel(session);

        Assert.False(vm.SetMaxAngleCommand.CanExecute("900"));
        Assert.False(vm.CenterCommand.CanExecute(null));

        await session.ConnectAsync();
        Assert.True(vm.SetMaxAngleCommand.CanExecute("900"));
        Assert.True(vm.CenterCommand.CanExecute(null));

        await session.DisconnectAsync();
        Assert.False(vm.SetMaxAngleCommand.CanExecute("900"));
        Assert.False(vm.CenterCommand.CanExecute(null));
    }

    [Fact]
    public async Task MotionRange_Loads_From_Device_On_Connect()
    {
        var transport = new FakeTransport(); // ReadSettingAsync returns 900
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new DashboardViewModel(session);

        await session.ConnectAsync();

        Assert.Equal(900, vm.MotionRange);
    }

    // ---- interpolação do ângulo (o desenho não pode saltar entre amostras) ----

    [Fact]
    public void First_Angle_Sample_Is_Assumed_Immediately()
    {
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = 2700, Position = 0 });

        Assert.Equal(270.0, vm.AngleDegrees, precision: 3);   // sem animar desde o zero
    }

    [Fact]
    public void Small_Change_Is_Interpolated_Not_Jumped()
    {
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = 0, Position = 0 });
        transport.Emit(new BaseState { AngleDeciDeg = 300, Position = 0 });  // +30°

        Assert.Equal(0.0, vm.AngleDegrees, precision: 3);     // ainda não moveu: sem quadro, sem avanço

        vm.TickAngleAnimation(0.008);                          // um quadro de ~120 Hz
        Assert.True(vm.AngleDegrees > 0.0);                    // andou em direção ao alvo
        Assert.True(vm.AngleDegrees < 30.0);                   // mas NÃO saltou
    }

    [Fact]
    public void Interpolation_Converges_To_Target()
    {
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = 0, Position = 0 });
        transport.Emit(new BaseState { AngleDeciDeg = 300, Position = 0 });

        for (var i = 0; i < 100; i++)   // ~0,8 s de quadros
            vm.TickAngleAnimation(0.008);

        Assert.Equal(30.0, vm.AngleDegrees, precision: 3);
    }

    [Fact]
    public void Big_Jump_Is_Assumed_Immediately()
    {
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = 0, Position = 0 });
        transport.Emit(new BaseState { AngleDeciDeg = 4500, Position = 0 });  // +450° (ex.: Center)

        Assert.Equal(450.0, vm.AngleDegrees, precision: 3);   // instantâneo, não varre a tela
    }

    [Fact]
    public void Long_Frame_Gap_Does_Not_Overshoot()
    {
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = 0, Position = 0 });
        transport.Emit(new BaseState { AngleDeciDeg = 300, Position = 0 });

        vm.TickAngleAnimation(5.0);   // quadro absurdamente longo

        Assert.Equal(30.0, vm.AngleDegrees, precision: 3);   // chega no alvo, nunca passa dele
    }

    [Fact]
    public void Without_Frames_Angle_Follows_Base_Directly_Instead_Of_Freezing()
    {
        // Se ninguém avança os quadros (relógio da view parado), interpolar viraria CONGELAR: o
        // desenho ficaria preso até o alvo passar do limiar e então saltaria. Foi o sintoma da
        // bancada em 2026-08-10. Sem quadros, o ângulo tem de seguir a base direto.
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = 0, Position = 0 });

        // várias amostras seguidas SEM nenhum TickAngleAnimation
        for (var i = 1; i <= 10; i++)
            transport.Emit(new BaseState { AngleDeciDeg = (short)(i * 50), Position = 0 });  // +5° por amostra

        Assert.Equal(50.0, vm.AngleDegrees, precision: 3);   // acompanhou, não congelou em 0
    }

    [Fact]
    public void With_Frames_Interpolation_Stays_Active()
    {
        // O contrário: com quadros chegando, a interpolação continua valendo (não vira passa-direto).
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = 0, Position = 0 });

        for (var i = 0; i < 3; i++)
        {
            vm.TickAngleAnimation(0.008);
            transport.Emit(new BaseState { AngleDeciDeg = 300, Position = 0 });
        }

        Assert.True(vm.AngleDegrees > 0.0);    // andou
        Assert.True(vm.AngleDegrees < 30.0);   // mas ainda perseguindo — não assumiu direto
    }

    // ---- o ângulo exibido não pode ser "-0°" ----

    [Fact]
    public void Angle_Slightly_Negative_Shows_Zero_Not_Minus_Zero()
    {
        // -0,4° arredonda para -0,0 em ponto flutuante, e o formato "0" imprime "-0" — que não é um
        // ângulo que exista. Aparecia sempre que a base parava um triz à esquerda do centro.
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = -4, Position = 0 });   // -0,4°

        Assert.Equal("0°", vm.AngleText);
    }

    [Theory]
    [InlineData(0, "0°")]
    [InlineData(-4, "0°")]     // -0,4 → zero, sem sinal
    [InlineData(4, "0°")]      // +0,4 → zero
    [InlineData(-56, "-6°")]   // -5,6 → arredonda e MANTÉM o sinal quando é negativo de verdade
    [InlineData(2700, "270°")]
    public void Angle_Text_Formats_Correctly(short deciDeg, string esperado)
    {
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = deciDeg, Position = 0 });
        Assert.Equal(esperado, vm.AngleText);
    }
}
