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
    public void Telemetry_Updates_PositionPercent()
    {
        // O ÂNGULO não vem mais daqui — vem do relatório do jogo, a 1 kHz. A telemetria continua
        // trazendo o resto (posição em %, temperaturas, correntes), agora a 5 por segundo.
        var vm = New(out var transport);
        transport.Emit(new BaseState { AngleDeciDeg = 2700, Position = 5000 });

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

    // ---- o ângulo vem do relatório do JOGO, não da telemetria ----
    //
    // A telemetria chega a 5 por segundo e serve a dados de painel — temperatura, corrente, estado.
    // O ângulo vem do relatório que a base já manda para o jogo, mil vezes por segundo, pelo mesmo
    // endpoint e sem custo nenhum: ele é enviado de qualquer forma. Não há mais interpolação porque
    // não há mais o que suavizar — o valor chega mais rápido do que a tela desenha.
    //
    // Isso é o que permitiu a telemetria ficar lenta, e é aí que está o ganho: todos os travamentos
    // capturados pararam no envio dela, e a base passou 8,7 horas sem travar com ela desligada.

    [Fact]
    public void Angulo_Vem_Do_Relatorio_Do_Jogo()
    {
        var vm = New(out var transport);

        transport.EmitirAngulo(270.0);

        Assert.Equal(270.0, vm.AngleDegrees, precision: 3);
    }

    [Fact]
    public void Angulo_Acompanha_Sem_Interpolar()
    {
        var vm = New(out var transport);

        transport.EmitirAngulo(0.0);
        transport.EmitirAngulo(30.0);

        // Sem meio-termo: a 1 kHz a próxima amostra chega antes do próximo quadro da tela, então
        // interpolar só atrasaria o desenho em relação ao volante real.
        Assert.Equal(30.0, vm.AngleDegrees, precision: 3);
    }

    [Fact]
    public void Telemetria_Nao_Mexe_No_Angulo_Quando_O_Relatorio_Do_Jogo_Esta_Chegando()
    {
        // ⚠️ As duas fontes carregam a MESMA grandeza. Se ambas escrevessem, a de 5 Hz puxaria o
        // desenho para trás cinco vezes por segundo — um solavanco visível a cada amostra.
        var vm = New(out var transport);

        transport.EmitirAngulo(90.0);
        transport.Emit(new BaseState { AngleDeciDeg = 0, Position = 0 });   // telemetria diz outra coisa

        Assert.Equal(90.0, vm.AngleDegrees, precision: 3);
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
    [InlineData(0.0, "0°")]
    [InlineData(-0.4, "0°")]    // -0,4 → zero, sem sinal
    [InlineData(0.4, "0°")]     // +0,4 → zero
    [InlineData(-5.6, "-6°")]   // arredonda e MANTÉM o sinal quando é negativo de verdade
    [InlineData(270.0, "270°")]
    public void Angle_Text_Formats_Correctly(double graus, string esperado)
    {
        var vm = New(out var transport);
        transport.EmitirAngulo(graus);
        Assert.Equal(esperado, vm.AngleText);
    }
}
