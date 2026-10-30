using System.Globalization;
using Daignostic.Core.Capture;
using Daignostic.Core.Config;

namespace Daignostic.Core.Analysis;

public enum Severity
{
    Info,
    Low,
    Medium,
    High,
}

/// <summary>A deterministic observation. <see cref="Evidence"/> lists the fact keys that support it.</summary>
public sealed record Finding(string Id, Severity Severity, string Title, string Detail, IReadOnlyList<string> Evidence);

public sealed record AnalysisResult(
    CaptureInfo Info,
    int RunIndex,
    CaptureRun Run,
    FrameMetrics Metrics,
    IReadOnlyList<Stutter> Stutters,
    IReadOnlyList<Freeze> Freezes,
    BottleneckResult Bottleneck,
    FreezeContext? FreezeContext,
    GameSettings? Settings,
    IReadOnlyList<Finding> Findings,
    IReadOnlyDictionary<string, string> Facts);

/// <summary>
/// Turns a capture into metrics, detections, rule-based findings and a flat table of facts.
/// Everything here is deterministic. The AI only explains these results; it never measures.
/// </summary>
public static class CaptureAnalyzer
{
    public const double FreezeThresholdMs = 200.0;

    public static AnalysisResult Analyze(CaptureFile capture, int runIndex = 0, GameSettings? settings = null)
    {
        var run = capture.Runs[Math.Clamp(runIndex, 0, capture.Runs.Count - 1)];
        var metrics = FrameMetrics.Compute(run.FrameMs);
        var stutters = Detectors.FindStutters(run.TimeSec, run.FrameMs);
        var freezes = Detectors.FindFreezes(run.TimeSec, run.FrameMs, FreezeThresholdMs);
        var bottleneck = BottleneckAnalyzer.Analyze(run);
        var context = FreezeContextBuilder.Build(run, freezes);

        var facts = BuildFacts(capture.Info, run, metrics, stutters, freezes, bottleneck, context, settings);
        var findings = Rules.Evaluate(metrics, freezes, stutters, bottleneck, context, settings, facts);

        return new AnalysisResult(
            capture.Info, runIndex, run, metrics, stutters, freezes, bottleneck, context, settings, findings, facts);
    }

    private static Dictionary<string, string> BuildFacts(
        CaptureInfo info,
        CaptureRun run,
        FrameMetrics m,
        IReadOnlyList<Stutter> stutters,
        IReadOnlyList<Freeze> freezes,
        BottleneckResult b,
        FreezeContext? ctx,
        GameSettings? settings)
    {
        var f = new Dictionary<string, string>();
        void Put(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                f[key] = value;
            }
        }

        void PutNum(string key, double? value, string format = "0.#")
        {
            if (value is { } v && double.IsFinite(v))
            {
                f[key] = v.ToString(format, CultureInfo.InvariantCulture);
            }
        }

        Put("capture.game", info.Game);
        Put("capture.cpu", info.Cpu);
        Put("capture.gpu", info.Gpu);
        Put("capture.gpu_driver", info.GpuDriver);
        Put("capture.resolution", info.Resolution);
        Put("capture.api", info.GraphicsApi);
        Put("capture.presentation_mode", info.PresentationMode);
        Put("capture.system_ram", info.SystemRam);
        PutNum("capture.duration_sec", m.DurationSec, "0");

        PutNum("fps.average", m.AverageFps);
        PutNum("fps.median", m.MedianFps);
        PutNum("fps.p1", m.P1Fps);
        PutNum("fps.p0_2", m.P02Fps);
        PutNum("fps.low1_average", m.Low1AverageFps);
        PutNum("fps.low0_1_average", m.Low01AverageFps);
        PutNum("fps.min", m.MinFps);
        PutNum("fps.max", m.MaxFps);

        PutNum("stutter.count", stutters.Count, "0");
        PutNum("freeze.count", freezes.Count, "0");
        if (freezes.Count > 0)
        {
            PutNum("freeze.max_ms", freezes.Max(x => x.FrameMs), "0");
            Put("freeze.times_sec", string.Join(", ", freezes.Take(12).Select(x => x.TimeSec.ToString("0", CultureInfo.InvariantCulture))));
        }

        Put("bottleneck.verdict", b.Verdict.ToString());
        Put("bottleneck.confidence", b.Confidence.ToString());
        PutNum("gpu.busy_pct", b.GpuBusyPct);
        PutNum("gpu.load_avg", b.GpuLoadAvg);
        PutNum("cpu.busiest_thread_load_avg", b.CpuMaxThreadLoadAvg);
        PutNum("cpu.total_load_avg", b.CpuTotalLoadAvg);
        PutNum("gpu.temp_max_c", b.GpuTempMax);
        PutNum("gpu.hotspot_max_c", b.GpuHotSpotMax);
        PutNum("cpu.temp_max_c", b.CpuTempMax);
        PutNum("gpu.power_limit_hits_pct", b.PowerLimitHitsPct);
        PutNum("vram.used_max_gb", b.VramUsedMaxGb);
        PutNum("vram.total_gb", b.VramTotalGb);

        if (ctx is not null)
        {
            foreach (var s in ctx.Sensors)
            {
                var key = "freeze_ctx." + Slug(s.Label);
                PutNum(key + ".at_freeze_avg", s.AtFreezeAverage);
                PutNum(key + ".at_freeze_max", s.AtFreezeMax);
                PutNum(key + ".overall_avg", s.OverallAverage);
            }
        }

        if (settings is not null)
        {
            foreach (var item in settings.Items)
            {
                Put("settings." + item.Key, item.Value);
            }
        }

        return f;
    }

    internal static string Slug(string label)
    {
        var chars = label.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
        return string.Join("_", new string(chars).Split('_', StringSplitOptions.RemoveEmptyEntries));
    }
}
