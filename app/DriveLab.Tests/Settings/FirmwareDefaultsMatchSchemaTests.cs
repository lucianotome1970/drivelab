// ============================================================================
//  DriveLab
//  FirmwareDefaultsMatchSchemaTests.cs — Os defaults do firmware e os do app têm
//  de ser o MESMO número.
//
//  POR QUE EXISTE: são duas listas escritas à mão, em linguagens diferentes, que
//  precisam concordar campo a campo — o `def[]` de firmware-base/src/a0_channel.cpp
//  e o BaseSettingsSchema daqui. Nada as ligava, e elas divergiram: a corrente de
//  calibração ficou 30 A no firmware e 3 A no app, enquanto o valor que a placa
//  realmente usava era 5 A. Passou dias assim, e só apareceu porque alguém leu os
//  dois lados no mesmo dia por acaso.
//
//  O estrago desse tipo de divergência é silencioso: o app mostra um número, a
//  base usa outro, e quem monta ajusta no escuro achando que vê o que a placa faz.
//
//  Este teste lê o .cpp do firmware como texto. É deliberado — gerar um dos lados
//  a partir do outro seria mais bonito, mas acopla o build do app ao do firmware.
//  Ler e comparar mantém os dois independentes e ainda assim honestos.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Globalization;
using System.Text.RegularExpressions;
using DriveLab.Core.Settings;

namespace DriveLab.Tests.Settings;

public class FirmwareDefaultsMatchSchemaTests
{
    /// <summary>Índices que existem só no firmware: guardam estado interno e não são settings do
    /// usuário, então não têm (nem devem ter) entrada no schema do app.</summary>
    private static readonly HashSet<int> SomenteFirmware = new()
    {
        12,   // current_p  — vago de propósito: o ODrive DERIVA o ganho do motor e da banda
        13,   // current_i  — idem. Os ids não são reaproveitados para não reinterpretar valor salvo
        47,   // build_id   — estado interno da trava de bring-up (ver bringup_lock.h)
    };

    // ---- localização do arquivo do firmware -------------------------------------------------

    private static string AcharRaizDoRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "firmware-base"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "não achei a raiz do repositório (subindo a partir de " + AppContext.BaseDirectory + ")");
    }

    private static string LerA0Channel()
    {
        var caminho = Path.Combine(AcharRaizDoRepo(), "firmware-base", "src", "a0_channel.cpp");
        Assert.True(File.Exists(caminho), $"não achei {caminho}");
        return File.ReadAllText(caminho);
    }

    // ---- parsing ----------------------------------------------------------------------------

    private static string TirarComentarios(string s)
    {
        s = Regex.Replace(s, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        s = Regex.Replace(s, @"//[^\n]*", " ");
        return s;
    }

    /// <summary>O `s_idef[]` do firmware, na ordem — índice = BaseSettingId.</summary>
    private static int[] LerDefaultsInteiros(string fonte)
    {
        var m = Regex.Match(fonte,
            @"static\s+const\s+int32_t\s+s_idef\s*\[\s*A0_NUM_SETTINGS\s*\]\s*=\s*\{(.*?)\};",
            RegexOptions.Singleline);
        Assert.True(m.Success, "não achei o array s_idef[] em a0_channel.cpp");

        var corpo = TirarComentarios(m.Groups[1].Value);
        return corpo.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(t => t.Length > 0)
                    .Select(t => int.Parse(t, CultureInfo.InvariantCulture))
                    .ToArray();
    }

    /// <summary>Os campos float não moram no s_idef[]: são atribuições soltas em s_fdef[i].</summary>
    private static Dictionary<int, double> LerDefaultsFloat(string fonte)
    {
        var limpo = TirarComentarios(fonte);
        var achados = new Dictionary<int, double>();
        foreach (Match m in Regex.Matches(limpo, @"s_fdef\s*\[\s*(\d+)\s*\]\s*=\s*([-\d.]+)f?\s*;"))
        {
            var idx = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            achados[idx] = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        return achados;
    }

    private static int LerNumSettings(string fonte)
    {
        var m = Regex.Match(fonte, @"#define\s+A0_NUM_SETTINGS\s+(\d+)");
        Assert.True(m.Success, "não achei #define A0_NUM_SETTINGS");
        return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    // ---- os testes --------------------------------------------------------------------------

    /// <summary>O array tem exatamente o tamanho que o #define promete. Um a menos e o firmware lê
    /// lixo de pilha no último campo; um a mais e alguém acrescentou sem atualizar a contagem.</summary>
    [Fact]
    public void Def_Tem_O_Tamanho_Declarado()
    {
        var fonte = LerA0Channel();
        var esperado = LerNumSettings(fonte);
        var defs = LerDefaultsInteiros(fonte);

        Assert.True(defs.Length == esperado,
            $"A0_NUM_SETTINGS diz {esperado}, mas def[] tem {defs.Length} valores");
    }

    /// <summary>Todo setting do app existe no firmware, e todo campo do firmware que não seja
    /// interno tem entrada no app. Divergência aqui é campo fantasma numa das pontas.</summary>
    [Fact]
    public void Os_Dois_Lados_Tem_Os_Mesmos_Campos()
    {
        var fonte = LerA0Channel();
        var total = LerNumSettings(fonte);

        var noApp = BaseSettingsSchema.All.Select(d => (int)d.Id).ToHashSet();
        var noFirmware = Enumerable.Range(0, total).Where(i => !SomenteFirmware.Contains(i)).ToHashSet();

        var soNoApp      = noApp.Except(noFirmware).OrderBy(i => i).ToList();
        var soNoFirmware = noFirmware.Except(noApp).OrderBy(i => i).ToList();

        Assert.True(soNoApp.Count == 0,
            "settings no app que o firmware não conhece (id): " + string.Join(", ", soNoApp));
        Assert.True(soNoFirmware.Count == 0,
            "campos no firmware sem entrada no schema do app (id): " + string.Join(", ", soNoFirmware) +
            " — se for estado interno, declare em SomenteFirmware com o porquê");
    }

    /// <summary>O NÚMERO tem de ser o mesmo dos dois lados. É este que pegaria o 30 contra 3.</summary>
    [Fact]
    public void Cada_Default_Bate_Campo_A_Campo()
    {
        var fonte = LerA0Channel();
        var defsInt = LerDefaultsInteiros(fonte);
        var defsFloat = LerDefaultsFloat(fonte);

        var divergencias = new List<string>();

        foreach (var d in BaseSettingsSchema.All)
        {
            var idx = (int)d.Id;
            if (idx >= defsInt.Length) continue;   // coberto por Def_Tem_O_Tamanho_Declarado

            // Campo float mora em s_fval; ausente lá significa default 0.
            double noFirmware = d.Type == SettingType.Float
                ? (defsFloat.TryGetValue(idx, out var f) ? f : 0.0)
                : defsInt[idx];

            if (Math.Abs(noFirmware - d.Default) > 1e-6)
                divergencias.Add($"{d.Id} (id {idx}, \"{d.Key}\"): app={d.Default}, firmware={noFirmware}");
        }

        Assert.True(divergencias.Count == 0,
            "default diferente entre o app e o firmware:\n  " + string.Join("\n  ", divergencias));
    }

    /// <summary>O default tem de ser um valor que o próprio campo aceita. Default fora da faixa faz
    /// a UI nascer mostrando um número que ela recusaria se você o digitasse.</summary>
    [Fact]
    public void Nenhum_Default_Cai_Fora_Da_Propria_Faixa()
    {
        var fora = BaseSettingsSchema.All
            .Where(d => d.Default < d.Min || d.Default > d.Max)
            .Select(d => $"{d.Id}: default={d.Default}, faixa=[{d.Min}, {d.Max}]")
            .ToList();

        Assert.True(fora.Count == 0, "default fora da faixa:\n  " + string.Join("\n  ", fora));
    }
}
