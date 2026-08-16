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
    public async Task Field_Is_Empty_Until_Loaded_And_Empties_On_Disconnect()
    {
        // A base é a fonte de verdade: sem ler da base, o campo não inventa o default do schema.
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.MotionRange));
        Assert.False(vm.IsLoaded);
        Assert.Equal("—", vm.ValueText);     // desconectado → vazio

        await session.ConnectAsync();
        await vm.LoadAsync();
        Assert.True(vm.IsLoaded);
        Assert.Equal("900", vm.ValueText);   // lido da base → mostra o valor real

        await session.DisconnectAsync();     // dispara Disconnected → campo volta ao "não lido"
        Assert.False(vm.IsLoaded);
        Assert.Equal("—", vm.ValueText);     // desconectou → volta ao vazio
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
    public void Clicar_No_Chip_De_Preset_Notifica_O_SLIDER()
    {
        // O slider le DisplayValue, nao Value. Sem a notificacao dele, clicar num chip mudava o
        // numero na caixa e deixava o slider parado onde estava — a tela contando duas historias
        // sobre o mesmo valor. Visto na bancada em 2026-08-12 com o chip "70" de Forca total.
        var vm = New(out _);
        var notificados = new List<string?>();
        vm.PropertyChanged += (_, e) => notificados.Add(e.PropertyName);

        vm.PresetOptions.First(p => p.Value == 720).SelectCommand.Execute(null);

        Assert.Equal(720, vm.Value);
        Assert.Equal(720, vm.DisplayValue);
        Assert.Contains(nameof(SettingFieldViewModel.DisplayValue), notificados);
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
    public async Task SelectPreset_Sets_Value_But_Does_Not_Write_Until_Saved()
    {
        // Escolher um preset só muda o valor na tela e marca pendência; quem envia à
        // placa é o "Salvar no controlador" (SettingsPageViewModel.SaveAsync).
        var vm = New(out var transport);
        await transport.ConnectAsync();
        vm.SelectPresetCommand.Execute("720");

        Assert.Equal(720, vm.Value);
        Assert.True(vm.IsModified);
        Assert.Null(transport.LastWrite);

        await vm.WriteAsync();               // é o que o Salvar faz
        Assert.Equal(BaseSettingId.MotionRange, transport.LastWrite!.Value.id);
        Assert.Equal(720, transport.LastWrite!.Value.value.AsDouble);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public void Integer_Setting_Is_Integer_And_Formats_Without_Decimals()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        // Campo NEUTRO de propósito: o EncoderCpr tem conversão de pulsos↔contagens em ABZ, então
        // ele testaria a conversão junto e não a formatação.
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.PolePairs));

        Assert.True(vm.IsInteger);
        vm.IsLoaded = true;   // simula "já lido da base" (sem load o campo exibe "—")
        vm.Value = 12.81;
        Assert.Equal("13", vm.ValueText); // "0" format rounds to nearest integer
    }

    [Fact]
    public void Float_Setting_Is_Not_Integer()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.TorqueConstant));

        Assert.False(vm.IsInteger);
    }

    [Fact]
    public void EncoderType_Lista_Os_Sensores_Do_Catalogo()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderType));

        // As opções vêm do catálogo: acrescentar um sensor lá aparece aqui sozinho.
        Assert.True(vm.HasOptions);
        Assert.Equal(EncoderCatalog.Models.Count, vm.Options.Count);
        // Passou de 2 opções, então a tela vira dropdown em vez de fileira de chips.
        Assert.True(vm.IsDropdown);
        // O primeiro da lista e um sensor com NOME — o "generico" saiu do catalogo.
        Assert.Equal(EncoderCatalog.E6b2, vm.Options[0].Value);
        Assert.DoesNotContain(vm.Options, o => o.Value == EncoderCatalog.Generico);
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
        // A opcao vem do catalogo, entao o teste compara com o catalogo — nao com um numero
        // cravado, que quebraria a cada sensor acrescentado ou removido da lista.
        Assert.Equal(EncoderCatalog.Models[1].Id, vm.Value);
        await vm.WriteAsync();
        Assert.Equal(BaseSettingId.EncoderType, transport.LastWrite!.Value.id);
        Assert.Equal(EncoderCatalog.Models[1].Id, transport.LastWrite!.Value.value.AsDouble);
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

    // Esta lógica JÁ SE PERDEU uma vez (o dropdown sumiu num merge e o tipo de encoder virou dois
    // botões). O teste existe para não se perder de novo: quando o catálogo de encoders crescer
    // além de duas opções, o campo tem que virar dropdown sozinho.
    [Fact]
    public void Enum_Curto_Vira_Chips_E_Nao_Dropdown()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.BoardVariant));

        Assert.Equal(2, vm.Options.Count);
        Assert.True(vm.HasChipOptions);
        Assert.False(vm.IsDropdown);
    }

    [Fact]
    public void SelectedOption_Mapeia_De_E_Para_O_Value()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.BoardVariant));

        vm.Value = 1;
        Assert.Equal(1, vm.SelectedOption?.Value);

        vm.SelectedOption = vm.Options[0];
        Assert.Equal(0, vm.Value);
    }

    [Fact]
    public void Tecnologia_Mostra_Apenas_O_Que_O_Sensor_Oferece()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var tech = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderInterface));

        // MT6835: ABZ e SPI, nunca SSI.
        tech.RefreshOptions(EncoderCatalog.Mt6835);
        Assert.Equal(2, tech.Options.Count);
        Assert.Contains(tech.Options, o => o.Value == (int)EncoderTech.Abz);
        Assert.Contains(tech.Options, o => o.Value == (int)EncoderTech.Spi);
        Assert.DoesNotContain(tech.Options, o => o.Value == (int)EncoderTech.Ssi);

        // E6B2: so ABZ.
        tech.RefreshOptions(EncoderCatalog.E6b2);
        Assert.Single(tech.Options);
    }

    [Fact]
    public void Trocar_De_Sensor_Corrige_Uma_Tecnologia_Invalida()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var tech = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderInterface));

        tech.RefreshOptions(EncoderCatalog.Mt6701);
        tech.Value = (int)EncoderTech.Ssi;         // valido no MT6701

        tech.RefreshOptions(EncoderCatalog.E6b2);  // E6B2 nao tem SSI
        Assert.Equal((int)EncoderTech.Abz, tech.Value);   // cai para o denominador comum
    }

    // O ×4 do ABZ e o erro numero 1 do forum: 68 topicos de gente digitando 600 onde a placa
    // precisa de 2400. A conta passa a ser do app; a pessoa digita o que esta impresso no encoder.
    [Fact]
    public void Abz_Pede_Ppr_E_Multiplica_Por_Quatro()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var cpr = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderCpr));

        cpr.ApplyEncoderTech(EncoderTech.Abz, EncoderCatalog.Generico);
        cpr.DisplayValue = 600;                 // o numero impresso no encoder

        Assert.Equal(2400, cpr.Value);          // o que vai para a placa
        Assert.Contains("PPR", cpr.DisplayLabel);
    }

    [Fact]
    public void Spi_Usa_O_Valor_Direto_Sem_Conta()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var cpr = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderCpr));

        cpr.ApplyEncoderTech(EncoderTech.Spi, EncoderCatalog.Generico);
        cpr.DisplayValue = 16384;

        Assert.Equal(16384, cpr.Value);
        Assert.DoesNotContain("PPR", cpr.DisplayLabel);
    }

    [Fact]
    public void Ppr_Mostrado_E_O_Cpr_Dividido_Por_Quatro()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var cpr = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderCpr));

        cpr.ApplyEncoderTech(EncoderTech.Abz, EncoderCatalog.Generico);
        cpr.Value = 10000;                      // veio da placa (E6B2)

        Assert.Equal(2500, cpr.DisplayValue);   // a pessoa ve o numero do datasheet
    }

    // Regressao: as opcoes recriadas precisam nascer com a selecao correta. Sem isso, clicar na
    // tecnologia que JA e o valor atual nao fazia nada — nao havia mudanca de valor para disparar
    // a sincronizacao, e a chip ficava sem marcar.
    [Fact]
    public void RefreshOptions_Marca_A_Opcao_Ja_Selecionada()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var tech = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderInterface));
        tech.IsLoaded = true;

        tech.RefreshOptions(EncoderCatalog.Mt6835);   // ABZ e SPI; valor cai em ABZ (0)

        var abz = tech.Options.Single(o => o.Value == (int)EncoderTech.Abz);
        Assert.True(abz.IsSelected);
    }

    // Um MT6701 em SSI tem 14 bits: exatamente 16384. Nao existe 16393 nesse sensor, entao a tela
    // nao pode deixar digitar — o numero e do silicio, nao uma preferencia.
    [Fact]
    public void Resolucao_De_Sensor_Conhecido_Em_Ssi_Nao_Se_Digita()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var cpr = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderCpr));

        cpr.ApplyEncoderTech(EncoderTech.Ssi, EncoderCatalog.Mt6701);

        Assert.True(cpr.IsValueLocked);
        Assert.Equal(16384, cpr.DisplayMax);
    }

    // Em ABZ a resolucao dos magneticos e PROGRAMAVEL no chip, entao continua editavel — quem
    // reprogramou o sensor precisa poder corrigir.
    //
    // ⚠️ E no MT6835 ela nao pode nem ter TETO por modelo: o modulo e vendido em variantes com a
    // saida AB gravada de fabrica (o manual lista AB-8192, AB-10000 e AB-16384), entao qualquer teto
    // cravado impediria alguem de digitar a resolucao do modulo que comprou. Foi o que aconteceu na
    // bancada em 15/08/2026 — o campo parava em 4096 pulsos e o modulo era AB-16384.
    [Fact]
    public void Resolucao_Em_Abz_Continua_Editavel_E_Sem_Teto_De_Modelo_No_Mt6835()
    {
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var cpr = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderCpr));

        cpr.ApplyEncoderTech(EncoderTech.Abz, EncoderCatalog.Mt6835);

        Assert.False(cpr.IsValueLocked);
        // Sem valor de fabrica, o limite passa a ser o do campo (u32), e o AB-16384 cabe com folga.
        Assert.True(cpr.DisplayMax >= 16384,
                    $"o campo precisa aceitar 16384 pulsos (AB-16384), mas para em {cpr.DisplayMax}");
    }

    [Fact]
    public void Padrao_Do_Firmware_Zero_Vira_Um_Sensor_Da_Lista()
    {
        // O firmware responde 0 no modelo do encoder — era a antiga opcao "generico", que saiu da
        // lista. Sem traduzir, o campo ficaria SEM nenhuma opcao marcada: tela em branco logo apos
        // clicar em "Padrao", que e o pior resultado para um botao que promete valor conhecido.
        var transport = new FakeTransport();
        var session = new BaseSession(transport, new ImmediateUiDispatcher());
        var vm = new SettingFieldViewModel(session, BaseSettingsSchema.Get(BaseSettingId.EncoderType));

        vm.ApplyDefaultFromDevice(EncoderCatalog.Generico);

        Assert.Equal(EncoderCatalog.E6b2, vm.Value);
        Assert.Contains(vm.Options, o => o.Value == (int)vm.Value);   // existe na lista
    }
}
