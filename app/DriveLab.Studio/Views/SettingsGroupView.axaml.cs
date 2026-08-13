// ============================================================================
//  DriveLab
//  SettingsGroupView.axaml.cs — Code-behind de SettingsGroupView. Expõe Header e Footer: conteúdo opcional
//  renderizado DENTRO do scroll, acima e abaixo dos campos, para que role JUNTO com eles.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace DriveLab.Studio.Views;

public partial class SettingsGroupView : UserControl
{
    /// <summary>Quantos pixels a página anda por "clique" da roda quando a devolvemos na mão.
    /// Valor do próprio ScrollViewer do Avalonia para rolagem por linha.</summary>
    private const double PixelsPorCliqueDaRoda = 50.0;

    // ------------------------------------------------------------------------------------------
    // A RODA DO MOUSE NÃO PODE MUDAR SETTING
    //
    // O NumericUpDown trata a roda como incremento quando o ponteiro está sobre ele. Numa aba longa
    // — a Hardware é — a pessoa rola a página para chegar no botão Salvar, o cursor passa por cima
    // de um campo, e o valor muda SEM ELA PERCEBER.
    //
    // Medido na bancada em 2026-08-10: o usuário digitou 8 A de corrente de calibração, clicou em
    // Salvar, e a placa recebeu 9. Na segunda tentativa, recebeu 10. Passamos um bom tempo achando
    // que era bug de persistência no firmware — o save sempre funcionou; o valor é que chegava
    // alterado. Num campo que configura HARDWARE isso é inaceitável: rolar a tela não pode
    // reconfigurar a máquina.
    //
    // Por que no TÚNEL e não no borbulho: no borbulho o handler só roda DEPOIS de o campo já ter
    // incrementado — tarde demais. No túnel chegamos antes, marcamos o evento como tratado (o campo
    // nunca o vê) e rolamos o ScrollViewer na mão, que era a intenção real do gesto.
    // ------------------------------------------------------------------------------------------
    private void RodaNaoMudaSetting(object? sender, PointerWheelEventArgs e)
    {
        if (e.Source is not Visual origem) return;

        // O ponteiro está sobre um campo numérico? (a origem costuma ser o TextBox interno dele)
        var campo = origem as NumericUpDown ?? origem.FindAncestorOfType<NumericUpDown>();
        if (campo is null) return;

        // Quem clicou dentro do campo está editando de propósito — aí a roda continua valendo.
        if (campo.IsKeyboardFocusWithin) return;

        e.Handled = true;   // o campo não vê a roda → o valor não muda

        var rolagem = RolagemDosCampos;
        if (rolagem is null) return;

        var limite = Math.Max(0.0, rolagem.Extent.Height - rolagem.Viewport.Height);
        var alvo = rolagem.Offset.Y - e.Delta.Y * PixelsPorCliqueDaRoda;
        rolagem.Offset = rolagem.Offset.WithY(Math.Clamp(alvo, 0.0, limite));
    }

    /// <summary>Conteúdo opcional exibido dentro do ScrollViewer, ACIMA das colunas de campos (rola junto).
    /// Usado pela aba Hardware para o monitor de telemetria rolar com os parâmetros, em vez de ficar fixo
    /// no topo comendo altura útil.</summary>
    public static readonly StyledProperty<object?> HeaderProperty =
        AvaloniaProperty.Register<SettingsGroupView, object?>(nameof(Header));

    public object? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Conteúdo opcional exibido dentro do ScrollViewer, ABAIXO das colunas de campos (rola junto).</summary>
    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<SettingsGroupView, object?>(nameof(Footer));

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public SettingsGroupView()
    {
        InitializeComponent();
        // Túnel: preciso ver a roda ANTES do campo numérico. Não dá para declarar no XAML — o
        // atributo de evento ali é sempre borbulho, que chega tarde demais.
        AddHandler(PointerWheelChangedEvent, RodaNaoMudaSetting, RoutingStrategies.Tunnel);
    }
}
