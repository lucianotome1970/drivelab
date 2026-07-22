// ============================================================================
//  DriveLab
//  AutoProfileService.cs — Detecta o jogo rodando e carrega o perfil casado em cada módulo (auto-perfil).
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using DriveLab.Core.Games;

namespace DriveLab.Studio.Services;

/// <summary>
/// Auto-perfil por jogo: a cada tick pergunta ao <see cref="GameDetector"/> qual sim está rodando; quando o
/// jogo MUDA, busca no <see cref="GameProfileMap"/> os perfis casados e os aplica em cada módulo (via os
/// delegates de aplicação, que no app setam <c>ProfileLibrary.SelectedName</c>). O laço só posta <see cref="Tick"/>
/// na thread de UI; o núcleo é síncrono e testável com um detector e delegates fajutos.
/// </summary>
public sealed class AutoProfileService
{
    private readonly GameDetector _detector;
    private readonly Func<GameProfileMap> _map;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action<string> _applyBase;
    private readonly Action<string> _applyWheel;
    private readonly Action<string> _applyPedals;
    private readonly Action<string> _applyHandbrake;

    private CancellationTokenSource? _cts;

    public AutoProfileService(
        GameDetector detector,
        Func<GameProfileMap> map,
        IUiDispatcher dispatcher,
        Action<string> applyBase,
        Action<string> applyWheel,
        Action<string> applyPedals,
        Action<string> applyHandbrake)
    {
        _detector = detector;
        _map = map;
        _dispatcher = dispatcher;
        _applyBase = applyBase;
        _applyWheel = applyWheel;
        _applyPedals = applyPedals;
        _applyHandbrake = applyHandbrake;
    }

    /// <summary>Liga/desliga a troca automática.</summary>
    public bool Enabled { get; set; }

    /// <summary>Consulta se HÁ alterações não salvas em algum módulo. Quando true, a troca automática é PULADA
    /// (em vez de sobrescrever silenciosamente o que o usuário estava ajustando) e <see cref="LastSwitchSkipped"/>
    /// fica true para a UI avisar. Null = sem guarda.</summary>
    public Func<bool>? HasUnsavedChanges { get; set; }

    /// <summary>A última troca detectada foi pulada por haver alterações não salvas.</summary>
    public bool LastSwitchSkipped { get; private set; }

    /// <summary>Jogo detectado no último tick (null = nenhum).</summary>
    public KnownGame? CurrentGame { get; private set; }

    /// <summary>Disparado quando o jogo detectado muda (inclui para null ao fechar).</summary>
    public event EventHandler<KnownGame?>? GameChanged;

    /// <summary>Um passo: detecta o jogo e, se mudou, aplica os perfis casados. Síncrono (testável).</summary>
    public void Tick()
    {
        if (!Enabled) return;

        var game = _detector.Detect();
        if (game?.Id == CurrentGame?.Id) return;   // sem mudança → não reaplica

        CurrentGame = game;
        GameChanged?.Invoke(this, game);
        if (game is null) return;

        // Guarda: não descartar ajustes não salvos por causa de uma troca automática.
        if (HasUnsavedChanges?.Invoke() == true)
        {
            LastSwitchSkipped = true;
            return;
        }
        LastSwitchSkipped = false;

        var profiles = _map().For(game.Id);
        if (profiles is null) return;

        if (!string.IsNullOrEmpty(profiles.Base)) _applyBase(profiles.Base!);
        if (!string.IsNullOrEmpty(profiles.Wheel)) _applyWheel(profiles.Wheel!);
        if (!string.IsNullOrEmpty(profiles.Pedals)) _applyPedals(profiles.Pedals!);
        if (!string.IsNullOrEmpty(profiles.Handbrake)) _applyHandbrake(profiles.Handbrake!);
    }

    public bool IsRunning => _cts is { IsCancellationRequested: false };

    /// <summary>Inicia o laço de detecção (posta Tick na thread de UI a cada `seconds`). Idempotente.</summary>
    public void Start(double seconds = 2.0)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = RunAsync(seconds, cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(double seconds, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                _dispatcher.Post(Tick);
        }
        catch (OperationCanceledException) { /* Stop() normal */ }
    }
}
