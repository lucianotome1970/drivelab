// ============================================================================
//  DriveLab
//  StartupDetector.cs — Roda a sequência de detecção de módulos (base e pedais) exibida no splash.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

using L = DriveLab.Studio.Localization.LocalizationManager;

namespace DriveLab.Studio.Services;

/// <summary>Um passo de progresso do splash: fração 0..1 e texto de status.</summary>
public sealed record StartupProgress(double Fraction, string Status);

/// <summary>Resultado da detecção de módulos no startup.</summary>
public sealed record StartupResult(bool BaseConnected, bool PedalsConnected);

/// <summary>
/// Roda a sequência de detecção de módulos exibida no splash: sonda a base
/// (volante) e depois os pedais, reportando progresso para a barra.
/// As sondas são injetadas para permitir stub agora e HID real depois.
/// </summary>
public sealed class StartupDetector
{
    private readonly Func<Task<bool>> _probeBase;
    private readonly Func<Task<bool>> _probePedals;
    private readonly int _stepDelayMs;

    public StartupDetector(Func<Task<bool>> probeBase, Func<Task<bool>> probePedals, int stepDelayMs = 900)
    {
        _probeBase = probeBase;
        _probePedals = probePedals;
        _stepDelayMs = stepDelayMs;
    }

    public async Task<StartupResult> RunAsync(IProgress<StartupProgress> progress)
    {
        progress.Report(new StartupProgress(0.05, L.Get("Splash_Starting")));
        await DelayAsync(_stepDelayMs / 2);

        progress.Report(new StartupProgress(0.15, L.Get("Splash_SearchingBase")));
        var baseConnected = await _probeBase();
        await DelayAsync(_stepDelayMs); // dwell: dá tempo de ver a busca da base
        progress.Report(new StartupProgress(0.5, L.Get(baseConnected ? "Splash_BaseFound" : "Splash_BaseNotFound")));
        await DelayAsync(_stepDelayMs / 3);

        progress.Report(new StartupProgress(0.6, L.Get("Splash_SearchingPedals")));
        var pedalsConnected = await _probePedals();
        await DelayAsync(_stepDelayMs); // dwell: busca dos pedais
        progress.Report(new StartupProgress(0.9, L.Get(pedalsConnected ? "Splash_PedalsFound" : "Splash_PedalsNotFound")));
        await DelayAsync(_stepDelayMs / 3);

        progress.Report(new StartupProgress(1.0, L.Get("Splash_Ready")));
        await DelayAsync(_stepDelayMs / 2); // deixa "Pronto" visível antes de abrir o app
        return new StartupResult(baseConnected, pedalsConnected);
    }

    private static Task DelayAsync(int ms) => ms > 0 ? Task.Delay(ms) : Task.CompletedTask;
}
