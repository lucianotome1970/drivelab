// ============================================================================
//  DriveLab
//  SettingsPageViewModelTests.cs — Testes do VM de página de settings da base (dirty-tracking do Salvar).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;
using DriveLab.Studio.Services;
using DriveLab.Studio.Tests.Services;
using DriveLab.Studio.ViewModels;
using Xunit;
using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.Tests.ViewModels;

public class SettingsPageViewModelTests
{
    private static (SettingsPageViewModel page, SettingsGroupViewModel group) Make()
    {
        var t = new FakeTransport();
        t.ConnectAsync().GetAwaiter().GetResult();        // já conectado (real = autodetect)
        var s = new BaseSession(t, new ImmediateUiDispatcher());
        var group = new SettingsGroupViewModel(s, "Básico", new[] { BaseSettingId.TotalStrength });
        var page = new SettingsPageViewModel(s, "Base", new[] { new PageTab("Básico", group) });
        return (page, group);
    }

    [Fact]
    public async Task Save_Enabled_Only_When_Dirty()
    {
        var (page, group) = Make();
        await group.LoadAsync();                          // lê da placa → não suja
        Assert.False(page.IsDirty);
        Assert.False(page.SaveCommand.CanExecute(null));  // nada alterado

        group.Fields[0].Value = 42;                       // usuário altera → escreve → dirty
        Assert.True(page.IsDirty);
        Assert.True(page.SaveCommand.CanExecute(null));

        await page.SaveCommand.ExecuteAsync(null);        // salva na flash
        Assert.False(page.IsDirty);                       // firmware == app
        Assert.False(page.SaveCommand.CanExecute(null));
        page.Dispose();
    }

    // ── O "Salvar" VERIFICA o que ficou gravado ────────────────────────────────────────────
    //
    // Gravar na memória permanente exige o motor PARADO (a escrita congela a CPU por ~250 ms), então
    // numa base presa tentando calibrar ela pode simplesmente não acontecer. Antes o app dizia
    // "gravou na flash" logo depois de MANDAR o comando, e a pessoa reiniciava e encontrava o valor
    // velho — da tela, indistinguível de "o app não salva". Aconteceu na bancada em 15/08/2026 e com
    // o primeiro tester.

    [Fact]
    public async Task Save_Confirma_Quando_A_Memoria_Permanente_Bate()
    {
        var (page, group) = Make();
        await group.LoadAsync();
        group.Fields[0].Value = 42;

        await page.SaveCommand.ExecuteAsync(null);

        Assert.False(page.IsDirty);                        // gravou: app == memória permanente
        Assert.Equal(L.Get("Settings_Saved"), page.MensagemDeSalvar);
        page.Dispose();
    }

    [Fact]
    public async Task Save_Avisa_Quando_Nao_Chegou_A_Gravar()
    {
        var (page, group, transporte) = MakeComTransporte();
        await group.LoadAsync();
        group.Fields[0].Value = 42;

        // A memória permanente continua com o valor ANTIGO — é o que acontece quando o motor não
        // para a tempo e a gravação não chega a rodar.
        transporte.SavedToReturn = new SettingValue(SettingType.UInt8, 7);

        await page.SaveCommand.ExecuteAsync(null);

        // ⚠️ CONTINUA "sujo" de propósito: o ajuste está VALENDO na base, mas não sobrevive a
        // reiniciar. Limpar a pendência aqui faria a tela dizer que está tudo salvo sobre um valor
        // que vai sumir — exatamente a mentira que este trabalho remove.
        Assert.True(page.IsDirty);
        Assert.Equal(L.Get("Settings_SaveBusy"), page.MensagemDeSalvar);
        page.Dispose();
    }

    private static (SettingsPageViewModel, SettingsGroupViewModel, FakeTransport) MakeComTransporte()
    {
        var t = new FakeTransport();
        t.ConnectAsync().GetAwaiter().GetResult();
        var s = new BaseSession(t, new ImmediateUiDispatcher());
        var group = new SettingsGroupViewModel(s, "Básico", new[] { BaseSettingId.TotalStrength });
        var page = new SettingsPageViewModel(s, "Base", new[] { new PageTab("Básico", group) });
        return (page, group, t);
    }

    // ── "Padrão" pergunta à BASE, não usa o padrão do app ──────────────────────────────────
    // Os padrões moram em dois lugares (array do firmware e schema do app). Coincidem hoje, mas
    // bastava editar um lado para o botão escrever valores diferentes dos de uma placa recém-
    // gravada, com os dois parecendo "o padrão". Perguntando, existe uma resposta só.

    [Fact]
    public async Task Padrao_Usa_O_Valor_Que_A_BASE_Respondeu()
    {
        var t = new FakeTransport();
        await t.ConnectAsync();
        t.DefaultToReturn = new SettingValue(SettingType.UInt8, 73);   // o que a placa diz ser padrão
        var s = new BaseSession(t, new ImmediateUiDispatcher());
        var group = new SettingsGroupViewModel(s, "Básico", new[] { BaseSettingId.TotalStrength });
        var page = new SettingsPageViewModel(s, "Base", new[] { new PageTab("Básico", group) });
        await group.LoadAsync();

        await page.ResetDefaultsCommand.ExecuteAsync(null);

        Assert.Equal(BaseSettingId.TotalStrength, t.LastDefaultAsked);   // perguntou
        Assert.Equal(73, group.Fields[0].Value);                          // e adotou a resposta
    }

    [Fact]
    public async Task Padrao_NAO_Grava_Na_Placa()
    {
        var t = new FakeTransport();
        await t.ConnectAsync();
        var s = new BaseSession(t, new ImmediateUiDispatcher());
        var group = new SettingsGroupViewModel(s, "Básico", new[] { BaseSettingId.TotalStrength });
        var page = new SettingsPageViewModel(s, "Base", new[] { new PageTab("Básico", group) });
        await group.LoadAsync();

        await page.ResetDefaultsCommand.ExecuteAsync(null);

        // Consulta pura: nenhum comando de gravação saiu. Quem grava é o Salvar.
        Assert.Null(t.LastCommand);
        Assert.True(page.IsDirty);                        // a tela mudou, então o Salvar habilita
    }
}
