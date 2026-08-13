// ============================================================================
//  DriveLab
//  AdvancedTabViewModel.cs — Aba Avançado: os ajustes finos mais a CURVA de
//  resposta da força, que é um gráfico e não um slider.
//
//  Os cinco pontos da curva (FfbCurve0..4) continuam no grupo, porque é de lá
//  que sai o carregamento da placa e o Salvar — mas saem das colunas de sliders:
//  quem os edita é o gráfico. Aparecer nos dois lugares seria o mesmo controle
//  duas vezes, com risco de a pessoa achar que são coisas diferentes.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Settings;
using DriveLab.Studio.Services;

namespace DriveLab.Studio.ViewModels;

public sealed class AdvancedTabViewModel : SettingsGroupViewModel
{
    private static readonly BaseSettingId[] DaCurva =
        ForceCurveViewModel.Ids;

    /// <summary>A curva de resposta, editada arrastando os pontos.</summary>
    public ForceCurveViewModel Curva { get; }

    public AdvancedTabViewModel(BaseSession session, string title, IEnumerable<BaseSettingId> ids)
        : base(session, title, ids)
    {
        Curva = new ForceCurveViewModel(Fields, session);
        DividirEmColunas(Fields.Where(f => !DaCurva.Contains(f.SettingId)).ToList());
    }

    public override void Dispose()
    {
        Curva.Dispose();
        base.Dispose();
    }
}
