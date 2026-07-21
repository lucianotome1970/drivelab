// ============================================================================
//  DriveLab
//  SimulatedTelemetrySource.cs — Fonte de telemetria falsa (varredura de RPM) para dev/teste sem jogo nem bancada.
//  Autor: Luciano Tomé <lucianotome1970@gmail.com>
//  Copyright (c) 2026 Luciano Tomé — Licença MIT
// ============================================================================

namespace DriveLab.Core.Telemetry;

/// <summary>
/// Fonte simulada: varre o RPM de 0 até o redline em rampa (dente-de-serra) e reinicia. Serve para desenvolver
/// e testar a cadeia rev-lights no Mac — e para o usuário VALIDAR de mesa (aro no USB, sem motor/56V): o RPM
/// sobe sozinho e a barra varre verde→amarelo→vermelho→flash. O relógio é injetável para testes determinísticos.
/// </summary>
public sealed class SimulatedTelemetrySource : IGameTelemetrySource
{
    private readonly Func<double> _nowSeconds;
    private readonly float _maxRpm;
    private readonly double _sweepSeconds;

    /// <param name="nowSeconds">Relógio monotônico em segundos (injetável para teste).</param>
    /// <param name="maxRpm">Redline simulado.</param>
    /// <param name="sweepSeconds">Duração de uma varredura 0→redline.</param>
    public SimulatedTelemetrySource(Func<double> nowSeconds, float maxRpm = 8000f, double sweepSeconds = 4.0)
    {
        _nowSeconds = nowSeconds;
        _maxRpm = maxRpm;
        _sweepSeconds = sweepSeconds > 0 ? sweepSeconds : 4.0;
    }

    public string Name => "Simulado";
    public bool IsAvailable => true;

    public bool TryRead(out GameTelemetry telemetry)
    {
        double phase = (_nowSeconds() % _sweepSeconds) / _sweepSeconds; // 0..1
        float rpm = (float)(phase * _maxRpm);
        telemetry = new GameTelemetry
        {
            Rpm = rpm,
            MaxRpm = _maxRpm,
            ShiftRpm = 0,
            Gear = 1 + (int)(phase * 6),
            SpeedKmh = (float)(phase * 300),
            Flag = GameFlag.None,
            PitLimiter = false,
            HasData = true,
        };
        return true;
    }

    public void Dispose() { }
}
