// ============================================================================
//  DriveLab
//  ForceCurveView.axaml.cs — Desenho e arrasto da curva de resposta da força.
//
//  Aqui mora SÓ a matemática de tela (pixel ↔ %) e o desenho. A regra da curva
//  — limites, o que cada ponto significa, como se interpola — fica no
//  ForceCurveViewModel, que é testado sem interface. Foi de propósito: curva
//  errada não aparece como erro na tela, aparece como volante com feel errado.
//
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Views;

public partial class ForceCurveView : UserControl
{
    private const double RaioPonto = 7;

    /// <summary>Ponto sendo arrastado (-1 = nenhum).</summary>
    private int _arrastando = -1;

    // ⚠️ NAO declarar um InitializeComponent proprio aqui. O Avalonia GERA um, e e ele que
    // preenche os campos de x:Name (PlotArea, Plot). Declarar o nosso esconde o gerado, os campos
    // ficam nulos, o construtor estoura ao assinar SizeChanged — e a ABA INTEIRA fica em branco,
    // sem erro visivel na tela. Foi assim que a aba Avancado nasceu vazia.
    public ForceCurveView()
    {
        InitializeComponent();
        PlotArea.SizeChanged += (_, _) => Redesenhar();
        DataContextChanged += (_, _) => Assinar();
    }

    private ForceCurveViewModel? Vm => DataContext as ForceCurveViewModel;

    private void Assinar()
    {
        if (Vm is null) return;
        foreach (var ponto in Vm.PontosDaCurva)
            ponto.PropertyChanged += (_, _) => Redesenhar();
        Redesenhar();
    }

    // ── Conversões tela ↔ curva ─────────────────────────────────────────────────────────────
    // A origem da curva é embaixo à esquerda (0% pedido, 0% entregue); a da tela é em cima à
    // esquerda. É por isso que o Y é invertido — errar isso desenha a curva de cabeça para baixo,
    // que é sutil o suficiente para passar despercebido numa curva quase simétrica.

    private double LarguraUtil => Math.Max(1, PlotArea.Bounds.Width  - RaioPonto * 2);
    private double AlturaUtil  => Math.Max(1, PlotArea.Bounds.Height - RaioPonto * 2);

    private Point ParaTela(double entradaPct, double saidaPct) => new(
        RaioPonto + entradaPct / 100.0 * LarguraUtil,
        RaioPonto + (1.0 - saidaPct / 100.0) * AlturaUtil);

    private double ParaSaidaPct(double y) =>
        (1.0 - (y - RaioPonto) / AlturaUtil) * 100.0;

    private void Redesenhar()
    {
        if (Vm is null || Plot is null) return;
        Plot.Children.Clear();

        // TryFindResource, nao FindResource: tema sem a chave nao pode derrubar o desenho — a aba
        // inteira sumiria, que e o mesmo sintoma que ja tivemos aqui.
        var texto = this.TryFindResource("DlText", out var recurso) && recurso is IBrush b
            ? b : Brushes.White;

        // Grade em 25%: dá a escala sem competir com a curva.
        for (var p = 0; p <= 100; p += 25)
        {
            var v = ParaTela(p, 0);
            var h = ParaTela(0, p);
            Plot.Children.Add(new Line
            {
                StartPoint = new Point(v.X, ParaTela(0, 100).Y), EndPoint = new Point(v.X, ParaTela(0, 0).Y),
                Stroke = texto, StrokeThickness = 1, Opacity = 0.12,
            });
            Plot.Children.Add(new Line
            {
                StartPoint = new Point(ParaTela(0, 0).X, h.Y), EndPoint = new Point(ParaTela(100, 0).X, h.Y),
                Stroke = texto, StrokeThickness = 1, Opacity = 0.12,
            });
        }

        // A REFERÊNCIA: sai o mesmo que entra. É contra ela que se lê a curva.
        Plot.Children.Add(new Line
        {
            StartPoint = ParaTela(0, 0), EndPoint = ParaTela(100, 100),
            Stroke = texto, StrokeThickness = 1, Opacity = 0.35,
            StrokeDashArray = new AvaloniaList<double> { 4, 4 },
        });

        // A curva é AMOSTRADA, não ligada ponto a ponto: o firmware interpola em Hermite cúbico, e
        // desenhar retas entre os 11 pontos mostraria cantos que a base não produz. 1% de passo é
        // mais fino que qualquer pixel da tela.
        var amostras = new List<Point>(101);
        for (var pct = 0; pct <= 100; pct++)
            amostras.Add(ParaTela(pct, Vm.Avaliar(pct)));

        Plot.Children.Add(new Polyline
        {
            Stroke = new SolidColorBrush(Color.Parse("#2196F3")),
            StrokeThickness = 2.5,
            Points = amostras,
        });

        // Os pontos, arrastáveis.
        foreach (var ponto in Vm.PontosDaCurva)
        {
            // O ponto fixo é desenhado menor e vazado: quem olha entende que ele não se pega, sem
            // precisar tentar arrastar e descobrir que não vai.
            var fixo = ponto.Indice == ForceCurveViewModel.PontoFixo;
            var raio = fixo ? RaioPonto * 0.6 : RaioPonto;
            var centro = ParaTela(ponto.Input, ponto.Output);
            var bola = new Ellipse
            {
                Width = raio * 2, Height = raio * 2,
                Fill = fixo ? Brushes.Transparent : new SolidColorBrush(Color.Parse("#2196F3")),
                Stroke = fixo ? texto : Brushes.White, StrokeThickness = 2,
                Opacity = fixo ? 0.5 : 1.0,
            };
            Canvas.SetLeft(bola, centro.X - raio);
            Canvas.SetTop(bola, centro.Y - raio);
            Plot.Children.Add(bola);
        }
    }

    // ── Arrasto ─────────────────────────────────────────────────────────────────────────────
    // Só o Y se move: a entrada de cada ponto é fixa (0/25/50/75/100%), porque é assim que o
    // firmware guarda a curva. Deixar arrastar na horizontal prometeria um controle que a base
    // não tem.

    private int PontoMaisProximo(double x)
    {
        if (Vm is null) return -1;
        var melhor = -1;
        var menor = double.MaxValue;
        foreach (var ponto in Vm.PontosDaCurva)
        {
            if (ponto.Indice == ForceCurveViewModel.PontoFixo) continue;   // o 0% não se arrasta
            var d = Math.Abs(ParaTela(ponto.Input, ponto.Output).X - x);
            if (d < menor) { menor = d; melhor = ponto.Indice; }
        }
        // Só pega se o clique foi perto de um ponto — senão um clique no vazio moveria algo longe.
        return menor <= LarguraUtil / 8 ? melhor : -1;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var p = e.GetPosition(PlotArea);
        _arrastando = PontoMaisProximo(p.X);
        if (_arrastando >= 0)
        {
            // A fotografia da curva sai AQUI, uma vez por arrasto — ver IniciarArrasto.
            Vm?.IniciarArrasto();
            Vm?.Arrastar(_arrastando, ParaSaidaPct(p.Y));
            e.Pointer.Capture(PlotArea);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_arrastando < 0) return;
        Vm?.Arrastar(_arrastando, ParaSaidaPct(e.GetPosition(PlotArea).Y));
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _arrastando = -1;
        Vm?.TerminarArrasto();
        e.Pointer.Capture(null);
    }
}
