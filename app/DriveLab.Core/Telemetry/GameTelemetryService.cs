// ============================================================================
//  DriveLab
//  GameTelemetryService.cs — Orquestra fontes de telemetria: seleciona a ativa, faz o tick e dirige os LEDs do aro.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using DriveLab.Core.Protocol;

namespace DriveLab.Core.Telemetry;

/// <summary>
/// Liga a telemetria de jogo aos rev-lights do aro. Mantém uma lista de fontes reais (ordenadas por prioridade),
/// escolhe a primeira disponível, e a cada <see cref="Tick"/> lê a telemetria, calcula a barra e monta o frame
/// (botões + barra) para enviar via <c>sendLeds</c>. O núcleo (<see cref="BuildFrame"/>) é puro e testável; o
/// laço em tempo real fica no <see cref="Start"/>. Para validar de mesa, aponte <see cref="ForcedSource"/> para
/// uma <see cref="SimulatedTelemetrySource"/>.
/// </summary>
public sealed class GameTelemetryService : IDisposable
{
    private readonly IReadOnlyList<IGameTelemetrySource> _sources;
    private readonly Func<WheelLedReport, Task> _sendLeds;
    private readonly Func<double> _nowSeconds;
    private readonly object _gate = new();

    private IReadOnlyList<WheelLedColor> _buttonColors = Array.Empty<WheelLedColor>();
    private CancellationTokenSource? _loopCts;

    public GameTelemetryService(
        IEnumerable<IGameTelemetrySource> sources,
        Func<WheelLedReport, Task> sendLeds,
        Func<double> nowSeconds)
    {
        _sources = sources.ToList();
        _sendLeds = sendLeds;
        _nowSeconds = nowSeconds;
    }

    /// <summary>Configuração de zonas/cores/shift. Trocável em runtime.</summary>
    public RevLightSettings Settings { get; set; } = RevLightSettings.Default;

    /// <summary>Brilho global do cordão (0-255).</summary>
    public byte Brightness { get; set; } = 200;

    /// <summary>Fonte forçada (ex.: simulada) — quando definida, ignora a auto-seleção das fontes reais.</summary>
    public IGameTelemetrySource? ForcedSource { get; set; }

    /// <summary>Nome da fonte que produziu o último tick (null = nenhuma disponível).</summary>
    public string? ActiveSourceName { get; private set; }

    /// <summary>True enquanto o laço em tempo real está rodando.</summary>
    public bool IsRunning => _loopCts is { IsCancellationRequested: false };

    /// <summary>Último quadro de telemetria (para exibir no app).</summary>
    public GameTelemetry LastTelemetry { get; private set; }

    /// <summary>Dispara a cada tick com a telemetria lida (pode vir em thread de fundo).</summary>
    public event EventHandler<GameTelemetry>? TelemetryUpdated;

    /// <summary>Atualiza as cores dos botões (0-9) que ficam por baixo dos rev-lights.</summary>
    public void SetButtonColors(IReadOnlyList<WheelLedColor> colors)
    {
        lock (_gate) _buttonColors = colors.ToArray();
    }

    /// <summary>Escolhe a fonte ativa: a forçada (se disponível), senão a primeira fonte real disponível.</summary>
    public IGameTelemetrySource? SelectSource()
    {
        if (ForcedSource is { IsAvailable: true }) return ForcedSource;
        foreach (var s in _sources)
            if (s.IsAvailable) return s;
        return null;
    }

    /// <summary>Núcleo puro de um tick: lê a fonte, calcula a barra e monta o frame completo. Sem I/O.</summary>
    public WheelLedReport BuildFrame(double timeSeconds, out GameTelemetry telemetry)
    {
        var src = SelectSource();
        ActiveSourceName = src?.Name;

        if (src is null || !src.TryRead(out telemetry))
            telemetry = default;   // HasData=false → barra apagada

        IReadOnlyList<WheelLedColor> buttons;
        lock (_gate) buttons = _buttonColors;

        var bar = RevLightMapper.Compute(telemetry, Settings, timeSeconds);
        return RevLightFrame.Build(buttons, bar, Brightness);
    }

    /// <summary>Um passo: monta o frame e o envia. Retorna a telemetria lida.</summary>
    public async Task<GameTelemetry> TickAsync()
    {
        var report = BuildFrame(_nowSeconds(), out var t);
        LastTelemetry = t;
        await _sendLeds(report).ConfigureAwait(false);
        TelemetryUpdated?.Invoke(this, t);
        return t;
    }

    /// <summary>Inicia o laço em tempo real na frequência dada (Hz). Idempotente.</summary>
    public void Start(double hz = 30)
    {
        Stop();
        var cts = new CancellationTokenSource();
        _loopCts = cts;
        int periodMs = (int)Math.Max(1, Math.Round(1000.0 / Math.Max(1, hz)));
        _ = RunLoopAsync(periodMs, cts.Token);
    }

    /// <summary>Para o laço. Não altera os LEDs (o chamador restaura as cores dos botões se quiser).</summary>
    public void Stop()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
    }

    private async Task RunLoopAsync(int periodMs, CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(periodMs));
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                try { await TickAsync().ConfigureAwait(false); }
                catch { /* fonte/transport instável: pula o tick, tenta no próximo */ }
            }
        }
        catch (OperationCanceledException) { /* Stop() normal */ }
    }

    public void Dispose()
    {
        Stop();
        foreach (var s in _sources) s.Dispose();
        ForcedSource?.Dispose();
    }
}
