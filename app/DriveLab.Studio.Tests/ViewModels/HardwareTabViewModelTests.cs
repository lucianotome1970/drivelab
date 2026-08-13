// ============================================================================
//  DriveLab
//  HardwareTabViewModelTests.cs — Testes da aba Hardware: o encadeamento
//  modelo -> tecnologia -> resolucao, e o aviso da corrente de calibracao.
//
//  POR QUE ESTES TESTES EXISTEM: esta e a classe que traduz "qual sensor voce
//  comprou" em numeros que vao para a placa, e e a que mais mexemos. Um erro
//  aqui nao aparece na tela como erro — aparece como um volante que le a posicao
//  errada, que e o defeito mais caro que este projeto ja teve.
//
//  Autor: Luciano Tome <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tome — Licenca MIT
// ============================================================================

using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;

namespace DriveLab.Studio.Tests.ViewModels;

public class HardwareTabViewModelTests
{
    private static HardwareTabViewModel Make()
    {
        var transport = new FakeTransport();
        transport.ConnectAsync().GetAwaiter().GetResult();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        return new HardwareTabViewModel(session, "Hardware", new[]
        {
            BaseSettingId.EncoderType, BaseSettingId.EncoderInterface, BaseSettingId.EncoderCpr,
            BaseSettingId.CalibrationCurrent, BaseSettingId.CurrentLim, BaseSettingId.TorqueConstant,
        });
    }

    private static SettingFieldViewModel Campo(HardwareTabViewModel vm, BaseSettingId id) =>
        vm.Fields.First(f => f.SettingId == id);

    // ── O sensor manda nas tecnologias oferecidas ────────────────────────────────────────────

    [Fact]
    public void Trocar_De_Sensor_Troca_As_Tecnologias_Oferecidas()
    {
        var vm = Make();
        var modelo = Campo(vm, BaseSettingId.EncoderType);
        var tec    = Campo(vm, BaseSettingId.EncoderInterface);

        modelo.Value = EncoderCatalog.E6b2;                 // optico: so A/B/Z
        Assert.Single(tec.Options);
        Assert.Equal((int)EncoderTech.Abz, tec.Options[0].Value);

        modelo.Value = EncoderCatalog.Mt6701;               // magnetico: A/B/Z + SSI
        Assert.Equal(2, tec.Options.Count);
        Assert.Contains(tec.Options, o => o.Value == (int)EncoderTech.Ssi);

        // O AS5047P NAO tem SSI — se aparecesse, a tela ofereceria uma ligacao que aquele chip
        // nao faz, e a pessoa fiaria o sensor por um caminho que nunca vai responder.
        modelo.Value = EncoderCatalog.As5047p;
        Assert.DoesNotContain(tec.Options, o => o.Value == (int)EncoderTech.Ssi);
        Assert.Contains(tec.Options, o => o.Value == (int)EncoderTech.Spi);
    }

    [Fact]
    public void Tecnologia_Invalida_No_Sensor_Novo_Cai_Para_Uma_Valida()
    {
        var vm = Make();
        var modelo = Campo(vm, BaseSettingId.EncoderType);
        var tec    = Campo(vm, BaseSettingId.EncoderInterface);

        modelo.Value = EncoderCatalog.Mt6701;
        tec.Value = (int)EncoderTech.Ssi;                   // escolhe SSI...
        modelo.Value = EncoderCatalog.E6b2;                 // ...e troca para um sensor que so tem ABZ

        // Nunca pode sobrar uma combinacao que o hardware nao tem.
        Assert.Equal((int)EncoderTech.Abz, (int)tec.Value);
    }

    // ── A resolucao acompanha o par sensor+tecnologia ────────────────────────────────────────

    [Fact]
    public void Resolucao_De_Silicio_E_Preenchida_E_Travada()
    {
        var vm = Make();
        Campo(vm, BaseSettingId.EncoderType).Value = EncoderCatalog.Mt6701;
        Campo(vm, BaseSettingId.EncoderInterface).Value = (int)EncoderTech.Ssi;

        var cpr = Campo(vm, BaseSettingId.EncoderCpr);
        Assert.Equal(16384, cpr.Value);      // 14 bits: vem do chip, nao se digita
        Assert.True(cpr.IsValueLocked);
    }

    [Fact]
    public void Em_ABZ_A_Resolucao_Fica_Livre_Para_Digitar()
    {
        var vm = Make();
        Campo(vm, BaseSettingId.EncoderType).Value = EncoderCatalog.Mt6701;
        Campo(vm, BaseSettingId.EncoderInterface).Value = (int)EncoderTech.Abz;

        var cpr = Campo(vm, BaseSettingId.EncoderCpr);
        // Em ABZ o PPR depende da variante e da gravacao na EEPROM do sensor — nao ha valor de
        // fabrica a cravar, e travar o campo impediria quem tem outra variante de corrigir.
        Assert.False(cpr.IsValueLocked);
    }

    // ── O aviso da calibracao diz o valor EFETIVO ────────────────────────────────────────────

    [Fact]
    public void Avisa_Quando_A_Calibracao_Passa_Do_Limite_De_Corrente()
    {
        var vm = Make();
        Campo(vm, BaseSettingId.CurrentLim).Value = 12;
        Campo(vm, BaseSettingId.CalibrationCurrent).Value = 15;   // acima do limite

        // O firmware rebaixa em silencio; sem este aviso a tela mostraria 15 A e a placa usaria 12.
        Assert.NotNull(vm.CorrenteAviso);
        Assert.Contains("12", vm.CorrenteAviso!);

        Campo(vm, BaseSettingId.CalibrationCurrent).Value = 8;    // dentro do limite
        Assert.Null(vm.CorrenteAviso);
    }
}
