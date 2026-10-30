using System.Globalization;
using Daignostic.Core.Config;

namespace Daignostic.Core.Analysis;

/// <summary>
/// Deterministic diagnostic rules. Each finding cites the fact keys it is based on, so the AI (and the user)
/// can always trace a statement back to a measured value.
/// </summary>
internal static class Rules
{
    public static IReadOnlyList<Finding> Evaluate(
        FrameMetrics metrics,
        IReadOnlyList<Freeze> freezes,
        IReadOnlyList<Stutter> stutters,
        BottleneckResult bottleneck,
        FreezeContext? ctx,
        GameSettings? settings,
        IReadOnlyDictionary<string, string> facts)
    {
        var list = new List<Finding>();

        string[] Ev(params string[] keys) => keys.Where(facts.ContainsKey).ToArray();
        void Add(string id, Severity severity, string title, string detail, params string[] keys) =>
            list.Add(new Finding(id, severity, title, detail, Ev(keys)));

        if (freezes.Count > 0)
        {
            var worst = freezes.Max(f => f.FrameMs);
            var severity = freezes.Count >= 10 || worst >= 500 ? Severity.High
                : freezes.Count >= 3 ? Severity.Medium
                : Severity.Low;
            Add("FREEZES", severity,
                $"{freezes.Count} freeze(s) of {CaptureAnalyzer.FreezeThresholdMs:0} ms or more",
                $"The worst freeze lasted {worst:0} ms. Freezes feel like the game hanging and hurt 0.1% lows far more than the average.",
                "freeze.count", "freeze.max_ms", "freeze.times_sec");
        }

        if (freezes.Count > 0 && ctx?.Find("GPU load") is { } gpu)
        {
            if (gpu.AtFreezeAverage >= 85)
            {
                Add("FREEZES_GPU_BUSY", Severity.Medium,
                    "The GPU was busy during the freezes",
                    $"GPU load averaged {F(gpu.AtFreezeAverage)}% around the freezes. That points to GPU-side or engine-side work " +
                    "(for example ray tracing, shader or asset work), not to a system-wide stall.",
                    "freeze_ctx.gpu_load.at_freeze_avg", "freeze_ctx.gpu_load.overall_avg");
            }
            else if (gpu.AtFreezeAverage < 50)
            {
                Add("FREEZES_GPU_IDLE", Severity.Medium,
                    "The GPU was mostly idle during the freezes",
                    $"GPU load averaged only {F(gpu.AtFreezeAverage)}% around the freezes, so the GPU was waiting. " +
                    "Look at the CPU, disk, memory or shader compilation.",
                    "freeze_ctx.gpu_load.at_freeze_avg", "freeze_ctx.gpu_load.overall_avg");
            }
        }

        if (freezes.Count > 0 && ctx is not null)
        {
            var drives = ctx.Drives.ToList();
            var busy = drives.FirstOrDefault(d => d.AtFreezeMax >= 70);
            if (busy is not null)
            {
                Add("DISK_ACTIVITY_AT_FREEZES", Severity.Medium,
                    "Disk activity spiked around the freezes",
                    $"{busy.Label} reached {F(busy.AtFreezeMax)}% near a freeze. Asset streaming from a slow or busy drive can cause this.",
                    "freeze_ctx." + CaptureAnalyzer.Slug(busy.Label) + ".at_freeze_max");
            }
            else if (drives.Count > 0)
            {
                Add("DISK_RULED_OUT", Severity.Info,
                    "Disk activity does not explain the freezes",
                    $"No drive exceeded {F(drives.Max(d => d.AtFreezeMax))}% activity near a freeze.",
                    drives.Select(d => "freeze_ctx." + CaptureAnalyzer.Slug(d.Label) + ".at_freeze_max").ToArray());
            }
        }

        if (bottleneck.VramUsedMaxGb is { } used && bottleneck.VramTotalGb is { } total && total > 0)
        {
            var ratio = used / total;
            if (ratio >= 0.85)
            {
                Add("VRAM_PRESSURE", ratio >= 0.95 ? Severity.High : Severity.Medium,
                    "Video memory is nearly full",
                    $"Peak VRAM use was {F(used)} of {F(total)} GB ({ratio * 100:0}%). Running out of VRAM causes hitching.",
                    "vram.used_max_gb", "vram.total_gb");
            }
        }

        switch (bottleneck.Verdict)
        {
            case BottleneckVerdict.ThermalThrottling:
                Add("THERMAL", Severity.High, "Thermal throttling", bottleneck.Reasoning, "gpu.temp_max_c", "gpu.hotspot_max_c");
                break;
            case BottleneckVerdict.PowerLimited:
                Add("POWER_LIMIT", Severity.Medium, "GPU power limit reached", bottleneck.Reasoning, "gpu.power_limit_hits_pct");
                break;
            case BottleneckVerdict.GpuBound:
                Add("GPU_BOUND", Severity.Info, "GPU-bound", bottleneck.Reasoning, "bottleneck.verdict", "gpu.load_avg", "gpu.busy_pct");
                break;
            case BottleneckVerdict.CpuBound:
                Add("CPU_BOUND", Severity.Info, "CPU-bound", bottleneck.Reasoning, "bottleneck.verdict", "cpu.busiest_thread_load_avg");
                break;
            case BottleneckVerdict.Balanced:
                Add("BALANCED", Severity.Info, "No single bottleneck", bottleneck.Reasoning, "bottleneck.verdict");
                break;
        }

        if (metrics.AverageFps > 0 && metrics.Low1AverageFps < metrics.AverageFps * 0.5)
        {
            Add("UNEVEN_FRAMETIMES", Severity.Medium,
                "1% lows are far below the average",
                $"The average is {F(metrics.AverageFps)} fps but the 1% low average is {F(metrics.Low1AverageFps)} fps, " +
                "so the game feels worse than the average suggests.",
                "fps.average", "fps.low1_average", "fps.low0_1_average");
        }

        if (settings?.IsOn("ray_tracing") == true && (freezes.Count >= 3 || metrics.Low1AverageFps < metrics.AverageFps * 0.5))
        {
            Add("RAY_TRACING_WITH_HITCHES", Severity.Medium,
                "Ray tracing is on while the game hitches",
                "This is a hypothesis to test, not a proven cause: record another run with ray tracing off and compare. " +
                "The game config was read at its last save and may differ from what was active during the capture.",
                "settings.ray_tracing", "freeze.count", "fps.low1_average");
        }

        if (freezes.Count == 0 && stutters.Count <= Math.Max(5, metrics.FrameCount / 5000) &&
            metrics.AverageFps > 0 && metrics.Low1AverageFps >= metrics.AverageFps * 0.5)
        {
            Add("SMOOTH", Severity.Info, "Frame delivery looks smooth",
                "No freezes and few stutters.", "freeze.count", "stutter.count", "fps.low1_average");
        }

        return list;
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
