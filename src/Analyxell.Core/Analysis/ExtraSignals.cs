using Analyxell.Core.Capture;

namespace Analyxell.Core.Analysis;

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
    double? MemJunctionMaxC,
    double? SyncQuantizedPct = null)
{
    /// <param name="fallbackRefreshHz">The display mode Windows reports now, used when the capture has no refresh-rate sensor.</param>
    public static ExtraSignals Compute(CaptureRun run, double? fallbackRefreshHz = null)
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
            foreach (var ms in FrameGenerationSignal.SeriesFor(run).Ms)
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
            ram?.Average, ram?.Max, virt?.Average, virt?.Max, junction?.Max,
            vrr ? null : SyncQuantized(run, refMax ?? fallbackRefreshHz));
    }

    /// <summary>
    /// With VSync on, the display only changes on a refresh, so nearly every gap between displayed frames is a whole
    /// number of refresh periods. Without VSync, or with variable refresh, the gaps are continuous. This is what lets a
    /// capture show that VSync is on even when the driver, not the game, applied it.
    /// </summary>
    private static double? SyncQuantized(CaptureRun run, double? hz)
    {
        if (hz is not > 20)
        {
            return null;
        }

        var gaps = (run.DisplayMs.Length >= 600 ? run.DisplayMs : run.FrameMs).Where(v => double.IsFinite(v) && v > 0).ToArray();
        if (gaps.Length < 600)
        {
            return null;
        }

        var period = 1000.0 / hz.Value;
        var onGrid = gaps.Count(g =>
        {
            var ratio = g / period;
            var whole = Math.Round(ratio);
            return whole >= 1 && Math.Abs(ratio - whole) <= 0.05;
        });
        return onGrid * 100.0 / gaps.Length;
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
