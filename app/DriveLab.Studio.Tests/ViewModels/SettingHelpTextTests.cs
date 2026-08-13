// ============================================================================
//  DriveLab
//  SettingHelpTextTests.cs — Garante que todo setting exibido na UI tem a explicação do "?".
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
public class SettingHelpTextTests
{
    private static SettingFieldViewModel Field(SettingDescriptor d) =>
        new(new BaseSession(new FakeTransport(), new ImmediateUiDispatcher()), d);

    /// <summary>Settings que NÃO viram um campo na tela: são editados por um controle próprio, que
    /// carrega a própria explicação. Os onze pontos da curva de força são desenhados como um
    /// gráfico (ForceCurveView), e o "?" fica no título dele — exigir onze textos idênticos aqui
    /// encheria a tela de repetição sem explicar mais nada.</summary>
    private static readonly HashSet<BaseSettingId> EditadosPorControleProprio =
        ForceCurveViewModel.Ids.ToHashSet();

    /// <summary>Todo campo que aparece numa aba precisa do texto do "?". Sem isto, um setting novo
    /// entra na tela mudo — e o usuário fica adivinhando o que o controle faz (foi o pedido de
    /// 2026-08-09: "cada config com um ícone de interrogação explicando e um exemplo").</summary>
    [Fact]
    public void Every_Setting_Shown_In_A_Tab_Has_Help_Text()
    {
        var mudos = new List<string>();
        foreach (var d in BaseSettingsSchema.All)
        {
            if (EditadosPorControleProprio.Contains(d.Id)) continue;
            var vm = Field(d);
            if (!vm.HasHelp)
                mudos.Add($"{d.Id} (aba {d.Tab})");
        }

        Assert.True(mudos.Count == 0,
            "settings sem texto de ajuda: " + string.Join(", ", mudos));
    }

    /// <summary>A explicação traz um exemplo — é o que torna o texto útil para quem não é do ramo.</summary>
    [Fact]
    public void Help_Text_Carries_An_Example_Or_A_Warning()
    {
        var semExemplo = new List<string>();
        foreach (var d in BaseSettingsSchema.All)
        {
            var help = Field(d).HelpText;
            if (help.Length == 0) continue;
            // "Ex.:" nos que valem hoje; os que ainda não têm efeito trazem o aviso no lugar.
            if (!help.Contains("Ex.:") && !help.Contains("E.g.") &&
                !help.Contains("[ATENCAO]") && !help.Contains("[NOTE]"))
                semExemplo.Add(d.Id.ToString());
        }

        Assert.True(semExemplo.Count == 0,
            "ajuda sem exemplo nem aviso: " + string.Join(", ", semExemplo));
    }
}
