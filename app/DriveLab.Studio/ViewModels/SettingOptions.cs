// ============================================================================
//  DriveLab
//  SettingOptions.cs — Opções (enum) de settings exibidas como chips rotulados.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Linq;
using DriveLab.Core.Settings;

namespace DriveLab.Studio.ViewModels;

/// <summary>Opções (enum) para settings que devem virar chips rotulados, análogo a SettingPresets.</summary>
public static class SettingOptions
{
    public sealed record EnumOptionSpec(int Value, string LabelKey);

    private static readonly IReadOnlyList<EnumOptionSpec> Empty = Array.Empty<EnumOptionSpec>();

    private static readonly Dictionary<BaseSettingId, IReadOnlyList<EnumOptionSpec>> Map = new()
    {
        // O MODELO do sensor vem do catálogo — assim acrescentar um sensor novo é acrescentar
        // uma linha lá, e a tela se ajusta sozinha (com mais de 2 itens ela vira dropdown).
        // O nome vai como "chave" de tradução de propósito: L.Get devolve a própria chave quando
        // não acha, e nome de sensor é nome de produto — não se traduz.
        [BaseSettingId.EncoderType] = EncoderCatalog.Models
            .Select(m => new EnumOptionSpec(m.Id, m.Name))
            .ToArray(),

        // A TECNOLOGIA nasce vazia: as opções dependem do modelo escolhido e são preenchidas
        // em tempo de execução por SettingFieldViewModel.RefreshOptions(modelo).
        [BaseSettingId.EncoderInterface] = Array.Empty<EnumOptionSpec>(),
        [BaseSettingId.BoardVariant] = new[]
        {
            new EnumOptionSpec(0, "Setting_BoardVariant_24V"),
            new EnumOptionSpec(1, "Setting_BoardVariant_56V"),
        },
    };

    public static IReadOnlyList<EnumOptionSpec> For(BaseSettingId id) =>
        Map.TryGetValue(id, out var v) ? v : Empty;
}
