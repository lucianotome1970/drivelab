// ============================================================================
//  DriveLab
//  PedalCurveEditor.axaml.cs — Code-behind do editor de curva de pedal: desenha e arrasta os 6 pontos e os handles de deadzone.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using DriveLab.Core.Settings;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Controls;

/// <summary>Editor de curva de pedal: 6 pontos com Y arrastável (X fixo) + 2 handles de deadzone.
/// Estilo SimPro/MOZA. Interação validada manualmente; mapeamento em métodos estáticos.</summary>
public partial class PedalCurveEditor : UserControl
{
    private const double Pad = 14;
    private const double HitRadius = 16;
    private static readonly Color Accent = Color.FromRgb(0xFF, 0x6A, 0x00);

    public static readonly StyledProperty<IReadOnlyList<PedalCurvePointViewModel>?> PointsProperty =
        AvaloniaProperty.Register<PedalCurveEditor, IReadOnlyList<PedalCurvePointViewModel>?>(nameof(Points));

    public static readonly StyledProperty<double> DeadzoneLowProperty =
        AvaloniaProperty.Register<PedalCurveEditor, double>(nameof(DeadzoneLow), 0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> DeadzoneHighProperty =
        AvaloniaProperty.Register<PedalCurveEditor, double>(nameof(DeadzoneHigh), 100, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> LiveInput01Property =
        AvaloniaProperty.Register<PedalCurveEditor, double>(nameof(LiveInput01));

    public static readonly StyledProperty<double> LiveOutput01Property =
        AvaloniaProperty.Register<PedalCurveEditor, double>(nameof(LiveOutput01));

    public static readonly StyledProperty<bool> EditEnabledProperty =
        AvaloniaProperty.Register<PedalCurveEditor, bool>(nameof(EditEnabled), true);

    public IReadOnlyList<PedalCurvePointViewModel>? Points
    {
        get => GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public double DeadzoneLow { get => GetValue(DeadzoneLowProperty); set => SetValue(DeadzoneLowProperty, value); }
    public double DeadzoneHigh { get => GetValue(DeadzoneHighProperty); set => SetValue(DeadzoneHighProperty, value); }
    public double LiveInput01 { get => GetValue(LiveInput01Property); set => SetValue(LiveInput01Property, value); }
    public double LiveOutput01 { get => GetValue(LiveOutput01Property); set => SetValue(LiveOutput01Property, value); }
    public bool EditEnabled { get => GetValue(EditEnabledProperty); set => SetValue(EditEnabledProperty, value); }

    private Canvas _surface = null!;
    private Ellipse? _liveDot;
    private int _dragPoint = -1;      // 0..5
    private int _dragDeadzone = -1;   // 0 = low, 1 = high

    public PedalCurveEditor()
    {
        InitializeComponent();
        _surface = this.FindControl<Canvas>("Surface")!;
        _surface.PointerPressed += OnPointerPressed;
        _surface.PointerMoved += OnPointerMoved;
        _surface.PointerReleased += OnPointerReleased;
        _surface.SizeChanged += (_, _) => Redraw();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PointsProperty)
        {
            if (change.OldValue is IReadOnlyList<PedalCurvePointViewModel> oldPts)
                foreach (var p in oldPts) p.PropertyChanged -= OnPointChanged;
            if (change.NewValue is IReadOnlyList<PedalCurvePointViewModel> newPts)
                foreach (var p in newPts) p.PropertyChanged += OnPointChanged;
            Redraw();
        }
        else if (change.Property == LiveInput01Property || change.Property == LiveOutput01Property)
            UpdateLiveDot();
        else if (change.Property == DeadzoneLowProperty || change.Property == DeadzoneHighProperty)
            Redraw();
    }

    private void OnPointChanged(object? sender, PropertyChangedEventArgs e) => Redraw();

    // --- mapeamento (testável) ---
    private double PlotW => _surface.Bounds.Width - 2 * Pad;
    private double PlotH => _surface.Bounds.Height - 2 * Pad;
    private double MapX(double t01) => Pad + t01 * PlotW;
    private double MapY(double v01) => Pad + (1 - v01) * PlotH;
    private double InvX(double x) => System.Math.Clamp((x - Pad) / PlotW, 0, 1);
    private double InvY(double y) => System.Math.Clamp(1 - (y - Pad) / PlotH, 0, 1);

    internal static double ValueToY(double value0100, double h) => Pad + (1 - value0100 / 100.0) * (h - 2 * Pad);
    internal static double YToValue(double y, double h) => System.Math.Clamp((1 - (y - Pad) / (h - 2 * Pad)) * 100.0, 0, 100);

    private void Redraw()
    {
        _surface.Children.Clear();
        _liveDot = null;
        if (Points is null || PlotW <= 0 || PlotH <= 0)
            return;

        var pts = Points.Select(p => p.Value).ToList();
        var lo = DeadzoneLow / 100.0;
        var hi = DeadzoneHigh / 100.0;

        // regiões de deadzone sombreadas
        AddDeadzoneRect(0, lo);
        AddDeadzoneRect(hi, 1);

        // área preenchida da curva (amostrada aplicando deadzone)
        var poly = new Polygon { Fill = new SolidColorBrush(Accent, 0.18), Stroke = new SolidColorBrush(Accent), StrokeThickness = 2 };
        const int samples = 40;
        for (var i = 0; i <= samples; i++)
        {
            var x01 = (double)i / samples;
            var norm = hi > lo ? System.Math.Clamp((x01 - lo) / (hi - lo), 0, 1) : 0;
            var y01 = PedalCurve.Evaluate(pts, norm);
            poly.Points.Add(new Point(MapX(x01), MapY(y01)));
        }
        poly.Points.Add(new Point(MapX(1), MapY(0)));
        poly.Points.Add(new Point(MapX(0), MapY(0)));
        _surface.Children.Add(poly);

        // handles dos 6 pontos (Y arrastável)
        for (var i = 0; i < Points.Count; i++)
        {
            var x = MapX((double)i / (Points.Count - 1));
            var y = MapY(Points[i].Value / 100.0);
            _surface.Children.Add(Handle(x, y, 7, Accent));
        }

        // handles de deadzone na base
        _surface.Children.Add(Handle(MapX(lo), MapY(0), 6, Colors.White));
        _surface.Children.Add(Handle(MapX(hi), MapY(0), 6, Colors.White));

        // ponto vivo
        _liveDot = Handle(0, 0, 6, Colors.White);
        _liveDot.Stroke = new SolidColorBrush(Accent);
        _liveDot.StrokeThickness = 2;
        _surface.Children.Add(_liveDot);
        UpdateLiveDot();
    }

    private void UpdateLiveDot()
    {
        if (_liveDot is null || PlotW <= 0) return;
        Canvas.SetLeft(_liveDot, MapX(LiveInput01) - _liveDot.Width / 2);
        Canvas.SetTop(_liveDot, MapY(LiveOutput01) - _liveDot.Height / 2);
    }

    private void AddDeadzoneRect(double from01, double to01)
    {
        if (to01 <= from01) return;
        var r = new Rectangle { Fill = new SolidColorBrush(Colors.Black, 0.35) };
        Canvas.SetLeft(r, MapX(from01));
        Canvas.SetTop(r, Pad);
        r.Width = MapX(to01) - MapX(from01);
        r.Height = PlotH;
        _surface.Children.Add(r);
    }

    private static Ellipse Handle(double cx, double cy, double radius, Color fill)
    {
        var e = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = new SolidColorBrush(fill) };
        Canvas.SetLeft(e, cx - radius);
        Canvas.SetTop(e, cy - radius);
        return e;
    }

    // --- interação ---
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!EditEnabled || Points is null) return;
        var p = e.GetPosition(_surface);
        _dragPoint = -1;
        _dragDeadzone = -1;

        for (var i = 0; i < Points.Count; i++)
        {
            var hx = MapX((double)i / (Points.Count - 1));
            var hy = MapY(Points[i].Value / 100.0);
            if (Dist(p, hx, hy) <= HitRadius) { _dragPoint = i; return; }
        }
        if (Dist(p, MapX(DeadzoneLow / 100.0), MapY(0)) <= HitRadius) { _dragDeadzone = 0; return; }
        if (Dist(p, MapX(DeadzoneHigh / 100.0), MapY(0)) <= HitRadius) { _dragDeadzone = 1; }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (Points is null) return;
        var p = e.GetPosition(_surface);
        if (_dragPoint >= 0)
        {
            Points[_dragPoint].Value = System.Math.Round(InvY(p.Y) * 100);
        }
        else if (_dragDeadzone == 0)
        {
            DeadzoneLow = System.Math.Round(System.Math.Min(InvX(p.X) * 100, DeadzoneHigh - 1));
        }
        else if (_dragDeadzone == 1)
        {
            DeadzoneHigh = System.Math.Round(System.Math.Max(InvX(p.X) * 100, DeadzoneLow + 1));
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragPoint = -1;
        _dragDeadzone = -1;
    }

    private static double Dist(Point p, double x, double y) =>
        System.Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
}
