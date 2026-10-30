using Daignostic.Core.Capture;

namespace Daignostic.Core.Analysis;

/// <summary>
/// Signals beyond frame time that the capture already contains: display refresh, presentation and tearing flags,
/// latency, motion pacing, and memory pressure.
/// </summary>
public sealed record ExtraSignals(
    double? RefreshMinHz,
    double? RefreshMaxHz,
    bool VrrEvidence,
    double? FramesAboveRefreshPct,
    double? TearingAllowedPct,
    double? SyncZeroPct,
    double? LatencyAvgMs,
    double? LatencyP95Ms,
    double? LatencyMaxMs,
    double? AnimErrorAvgMs,
    double? AnimErrorP99Ms,
    double? RamUsageAvgPct,
    double? RamUsageMaxPct,
    double? VirtualMemAvgPct,
    double? VirtualMemMaxPct,
    double? MemJunctionMaxC)
{
    public static ExtraSignals Compute(CaptureRun run)
    {
        double? refMin = null, refMax = null;
        var vrr = false;
        var refresh = run.FindSensor("Monitor Refresh Rate", "Frequency");
        if (refresh is not null)
        {
            var s = Stats.Summarize(refresh.Values);
            if (s.Count > 0 && s.Max > 0)
            {
                refMin = s.Min;
                refMax = s.Max;
                // A refresh rate that moves while the game runs is the signature of variable refresh (G-SYNC, FreeSync).
                vrr = s.Min < s.Max * 0.8;
            }
        }

        double? above = null;
        if (refMax is { } hz)
        {
            var n = 0;
            var over = 0;
            foreach (var ms in run.FrameMs)
            {
                if (ms > 0 && double.IsFinite(ms))
                {
                    n++;
                    if (1000.0 / ms > hz * 1.02)
                    {
                        over++;
                    }
                }
            }

            above = n == 0 ? null : over * 100.0 / n;
        }

        var latency = Finite(run.PcLatencyMs).Where(v => v > 0).Order().ToArray();
        var anim = Finite(run.AnimationErrorMs).Select(Math.Abs).Order().ToArray();
        var ram = Stat(run, "RAM Usage", "Load");
        var virt = Stat(run, "Virtual RAM Usage", "Load");
        var junction = Stat(run, "GPU Memory Junction", "Temperature");

        return new ExtraSignals(
            refMin, refMax, vrr, above,
            Pct(run.AllowsTearing, v => v >= 0.5),
            Pct(run.SyncInterval, v => v < 0.5),
            latency.Length == 0 ? null : latency.Average(),
            latency.Length == 0 ? null : Stats.Percentile(latency, 0.95),
            latency.Length == 0 ? null : latency[^1],
            anim.Length == 0 ? null : anim.Average(),
            anim.Length == 0 ? null : Stats.Percentile(anim, 0.99),
            ram?.Average, ram?.Max, virt?.Average, virt?.Max, junction?.Max);
    }

    private static IEnumerable<double> Finite(double[] values) => values.Where(double.IsFinite);

    private static double? Pct(double[] values, Func<double, bool> test)
    {
        var finite = Finite(values).ToArray();
        return finite.Length == 0 ? null : finite.Count(test) * 100.0 / finite.Length;
    }

    private static SeriesSummary? Stat(CaptureRun run, string name, string type)
    {
        var sensor = run.FindSensor(name, type);
        if (sensor is null)
        {
            return null;
        }

        var s = Stats.Summarize(sensor.Values);
        return s.Count == 0 ? null : s;
    }
}
