using DriveLab.Studio.Services;
using Xunit;

namespace DriveLab.Studio.Tests.Services;

public class StartupDetectorTests
{
    /// <summary>IProgress síncrono para asserção determinística (sem SynchronizationContext).</summary>
    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _on;
        public SyncProgress(Action<T> on) => _on = on;
        public void Report(T value) => _on(value);
    }

    private static (StartupResult result, List<StartupProgress> reports) Run(bool baseOk, bool pedalsOk)
    {
        var reports = new List<StartupProgress>();
        var detector = new StartupDetector(
            probeBase: () => Task.FromResult(baseOk),
            probePedals: () => Task.FromResult(pedalsOk),
            stepDelayMs: 0);

        var result = detector.RunAsync(new SyncProgress<StartupProgress>(reports.Add)).GetAwaiter().GetResult();
        return (result, reports);
    }

    [Fact]
    public void Reports_Reach_Full_Progress_At_The_End()
    {
        var (_, reports) = Run(baseOk: false, pedalsOk: false);

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1].Fraction, precision: 3);
        Assert.False(string.IsNullOrWhiteSpace(reports[^1].Status));
    }

    [Fact]
    public void Progress_Is_Monotonic_Non_Decreasing()
    {
        var (_, reports) = Run(baseOk: true, pedalsOk: true);

        for (var i = 1; i < reports.Count; i++)
            Assert.True(reports[i].Fraction >= reports[i - 1].Fraction,
                $"fraction dropped at {i}: {reports[i - 1].Fraction} -> {reports[i].Fraction}");
    }

    [Fact]
    public void Probes_Both_Modules_In_Order()
    {
        var (_, reports) = Run(baseOk: false, pedalsOk: false);
        var statuses = reports.Select(r => r.Status).ToList();

        var baseIdx = statuses.FindIndex(s => s.Contains("base", StringComparison.OrdinalIgnoreCase));
        var pedalsIdx = statuses.FindIndex(s => s.Contains("peda", StringComparison.OrdinalIgnoreCase));

        Assert.True(baseIdx >= 0, "deve procurar a base");
        Assert.True(pedalsIdx >= 0, "deve procurar os pedais");
        Assert.True(baseIdx < pedalsIdx, "base deve vir antes dos pedais");
    }

    [Fact]
    public void Result_Reflects_Probe_Outcomes()
    {
        var (connected, _) = Run(baseOk: true, pedalsOk: false);
        Assert.True(connected.BaseConnected);
        Assert.False(connected.PedalsConnected);

        var (none, _) = Run(baseOk: false, pedalsOk: false);
        Assert.False(none.BaseConnected);
        Assert.False(none.PedalsConnected);
    }
}
