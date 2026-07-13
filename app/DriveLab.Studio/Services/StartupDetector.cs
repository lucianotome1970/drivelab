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

    public StartupDetector(Func<Task<bool>> probeBase, Func<Task<bool>> probePedals, int stepDelayMs = 500)
    {
        _probeBase = probeBase;
        _probePedals = probePedals;
        _stepDelayMs = stepDelayMs;
    }

    public async Task<StartupResult> RunAsync(IProgress<StartupProgress> progress)
    {
        progress.Report(new StartupProgress(0.05, "Iniciando…"));
        await DelayAsync();

        progress.Report(new StartupProgress(0.15, "Procurando base…"));
        var baseConnected = await _probeBase();
        await DelayAsync();
        progress.Report(new StartupProgress(0.5, baseConnected ? "Base conectada" : "Base não encontrada"));

        progress.Report(new StartupProgress(0.6, "Procurando pedais…"));
        var pedalsConnected = await _probePedals();
        await DelayAsync();
        progress.Report(new StartupProgress(0.9, pedalsConnected ? "Pedais conectados" : "Pedais não encontrados"));

        progress.Report(new StartupProgress(1.0, "Pronto"));
        return new StartupResult(baseConnected, pedalsConnected);
    }

    private Task DelayAsync() => _stepDelayMs > 0 ? Task.Delay(_stepDelayMs) : Task.CompletedTask;
}
