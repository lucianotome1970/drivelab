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
}
