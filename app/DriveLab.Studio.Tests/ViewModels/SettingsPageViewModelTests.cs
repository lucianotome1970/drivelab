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
    public async Task Named_Profile_SaveAs_Then_Apply_Restores_Setting()
    {
        var t = new FakeTransport();
        t.ConnectAsync().GetAwaiter().GetResult();
        var s = new BaseSession(t, new ImmediateUiDispatcher());
        var group = new SettingsGroupViewModel(s, "Básico", new[] { BaseSettingId.TotalStrength });
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"baselib-{System.Guid.NewGuid():N}");
        var lib = new JsonNamedProfileStore<BaseProfile>("base", dir);
        var page = new SettingsPageViewModel(s, "Base", new[] { new PageTab("Básico", group) }, lib);
        try
        {
            await group.LoadAsync();
            group.Fields[0].Value = 80;
            page.ProfileLibrary.NewName = "GT3";
            await page.ProfileLibrary.SaveAsCommand.ExecuteAsync(null);
            Assert.Contains("GT3", page.ProfileLibrary.Profiles);

            group.Fields[0].Value = 30;                       // muda; aplicar restaura 80
            page.ProfileLibrary.SelectedName = null;
            page.ProfileLibrary.SelectedName = "GT3";
            for (var i = 0; i < 60 && (int)group.Fields[0].Value != 80; i++)
                await Task.Delay(5);
            Assert.Equal(80, (int)group.Fields[0].Value);
            page.Dispose();
        }
        finally { if (System.IO.Directory.Exists(dir)) System.IO.Directory.Delete(dir, true); }
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
