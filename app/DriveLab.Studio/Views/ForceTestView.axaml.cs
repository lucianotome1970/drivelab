// ============================================================================
//  DriveLab
//  ForceTestView.axaml.cs — Code-behind de ForceTestView.
//
//  ⚠️ NÃO declarar um InitializeComponent próprio: o Avalonia gera um, e declarar
//  o nosso esconde o gerado — os campos de x:Name ficam nulos e a aba inteira
//  aparece em branco, sem erro visível. Ver o aviso em ForceCurveView.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia.Controls;
using Avalonia.Threading;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Views;

public partial class ForceTestView : UserControl
{
    private ForceTestViewModel? _vmAssinada;

    public ForceTestView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Reassinar();
        Reassinar();
    }

    /// <summary>O DataContext chega DEPOIS do construtor (e pode trocar). Sem reassinar aqui, o
    /// evento seria assinado num VM nulo e o scroll nunca aconteceria — falha silenciosa, do tipo
    /// que só aparece na bancada.</summary>
    private void Reassinar()
    {
        if (_vmAssinada is not null) _vmAssinada.ResultadoPronto -= AoSairResultado;
        _vmAssinada = DataContext as ForceTestViewModel;
        if (_vmAssinada is not null) _vmAssinada.ResultadoPronto += AoSairResultado;
    }

    /// <summary>Rola até o cartão que acabou de responder.
    ///
    /// <para>O veredito nasce ABAIXO do cartão, e com descrição e preparo longos ele cai fora da
    /// área visível. Quem clicou em Rodar continua olhando o topo, não vê nada mudar e conclui que o
    /// teste não respondeu — foi exatamente o que aconteceu com o teste de encoder em 15/08/2026,
    /// com o resultado inteiro escrito na tela o tempo todo. Resposta que ninguém vê não é resposta.</para>
    ///
    /// <para>⚠️ O `Post` em prioridade Background não é enfeite: o veredito só ganha altura depois
    /// que o layout roda. Rolar no mesmo instante em que o texto é publicado mira uma posição que
    /// ainda não existe, e a tela para no lugar errado — geralmente logo acima do que interessa.</para></summary>
    private void AoSairResultado(ForceTestItemViewModel item)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_vmAssinada is null) return;
            var i = System.Linq.Enumerable.ToList(_vmAssinada.Testes).IndexOf(item);
            if (i < 0) return;
            (ListaDeTestes.ContainerFromIndex(i) as Control)?.BringIntoView();
        }, DispatcherPriority.Background);
    }
}
