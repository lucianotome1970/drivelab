// ============================================================================
//  DriveLab
//  ForceTestView.axaml.cs — Code-behind de ForceTestView (InitializeComponent).
//
//  ⚠️ NÃO declarar um InitializeComponent próprio: o Avalonia gera um, e declarar
//  o nosso esconde o gerado — os campos de x:Name ficam nulos e a aba inteira
//  aparece em branco, sem erro visível. Ver o aviso em ForceCurveView.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia.Controls;

namespace DriveLab.Studio.Views;

public partial class ForceTestView : UserControl
{
    public ForceTestView() => InitializeComponent();
}
