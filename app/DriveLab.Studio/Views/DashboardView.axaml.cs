// ============================================================================
//  DriveLab
//  DashboardView.axaml.cs — Code-behind de DashboardView: InitializeComponent + o relógio de
//  quadros que interpola o ângulo do volante (ver DashboardViewModel.TickAngleAnimation).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using Avalonia.Controls;
using Avalonia.Threading;
using DriveLab.Studio.ViewModels;

namespace DriveLab.Studio.Views;

public partial class DashboardView : UserControl
{
    // ~120 Hz: o dobro da taxa de tela comum, para a interpolação ficar suave também em monitores de
    // 144 Hz. O trabalho por tick é uma subtração e uma exponencial — irrelevante.
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(8);

    private readonly DispatcherTimer _frameTimer;
    private DateTime _lastFrame;

    public DashboardView()
    {
        InitializeComponent();

        // O desenho do volante recebe posição a ~60 Hz e saltava entre amostras. Este relógio avança
        // a interpolação a cada quadro; a lógica em si vive no ViewModel (testável, sem Avalonia).
        _frameTimer = new DispatcherTimer { Interval = FrameInterval };
        _frameTimer.Tick += OnFrame;

        // COMEÇA JÁ, e não só quando a view entra na árvore visual.
        //
        // A versão anterior só dava Start no AttachedToVisualTree. Quando esse evento não chega (ou
        // chega e o par Detached para o relógio sem ele voltar), a interpolação PARA — e aí o ângulo
        // exibido só muda nos saltos acima do limiar, o que na tela vira "congela e depois pula",
        // relatado na bancada em 2026-08-10. Um relógio de 8 ms que só faz uma subtração custa
        // praticamente nada; deixá-lo rodando é mais barato que o risco de ele não rodar.
        _lastFrame = DateTime.UtcNow;
        _frameTimer.Start();

        // Fora da árvore não há o que desenhar: para. Ao voltar, religa e zera o relógio (senão o
        // primeiro quadro traria o tempo inteiro em que ficou escondido).
        AttachedToVisualTree += (_, _) => { _lastFrame = DateTime.UtcNow; _frameTimer.Start(); };
        DetachedFromVisualTree += (_, _) => _frameTimer.Stop();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        // Um tick atrasado (app minimizado, GC, troca de aba) não deve fazer o ângulo dar um pulo:
        // limita o passo a 100 ms.
        if (dt > 0.1)
            dt = 0.1;

        (DataContext as DashboardViewModel)?.TickAngleAnimation(dt);
    }
}
