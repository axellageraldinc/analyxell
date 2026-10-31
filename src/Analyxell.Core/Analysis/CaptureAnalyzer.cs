using System.Globalization;
using Analyxell.Core.Background;
using Analyxell.Core.Capture;
using Analyxell.Core.Config;
using Analyxell.Core.SystemInfo;

namespace Analyxell.Core.Analysis;

public enum Severity
{
    Info,
    Low,
    Medium,
    High,
}

/// <summary>
/// A deterministic observation. <see cref="Evidence"/> lists the fact keys that support it and
/// <see cref="Suggestions"/> what the user can try. <see cref="LocalOnly"/> findings mention things such as program
/// names and are never put into AI messages (their anonymous numbers are sent as facts instead).
/// </summary>
public sealed record Finding(
    string Id,
    Severity Severity,
    string Title,
    string Detail,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Suggestions,
    bool LocalOnly = false);

/// <summary>One suggested action, ordered by how much it is likely to matter.</summary>
public sealed record NextStep(int Order, string Text, string FindingId, string FindingTitle, Severity Severity);

public sealed record AnalysisResult(
    CaptureInfo Info,
    int RunIndex,
    CaptureRun Run,
    FrameMetrics Metrics,
    IReadOnlyList<Stutter> Stutters,
    IReadOnlyList<Freeze> Freezes,
    BottleneckResult Bottleneck,
    FreezeContext? FreezeContext,
    ExtraSignals Extra,
    GameSettings? Settings,
    SystemContext System,
    BackgroundScanResult? Background,
    PcSpecs Specs,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<NextStep> NextSteps,
    IReadOnlyDictionary<string, string> Facts);

/// <summary>
/// Turns a capture into metrics, detections, rule-based findings with suggestions, and a flat table of facts.
/// Everything here is deterministic. The AI only explains these results; it never measures.
/// </summary>
public static class CaptureAnalyzer
{
    public const double FreezeThresholdMs = 200.0;

    public static AnalysisResult Analyze(
        CaptureFile capture,
        int runIndex = 0,
        GameSettings? settings = null,
        SystemContext? system = null,
        BackgroundScanResult? background = null,
        bool shareBackgroundNames = false,
        IReadOnlyList<ProfileItem>? gameFacts = null)
    {
        var run = capture.Runs[Math.Clamp(runIndex, 0, capture.Runs.Count - 1)];
        system ??= SystemContext.Empty;
        var frameGen = FrameGenerationSignal.Compute(run);

        // With generated frames in the present stream, smoothness is judged on what the screen shows. If the capture has
        // no display times, groups of presents are used so the long/short alternation is not counted as stutter.
        var series = FrameGenerationSignal.SeriesFor(run, frameGen);
        var metrics = FrameMetrics.Compute(series.Ms);
        var (stutterTimes, stutterMs) = series.FromDisplay ? (series.Times, series.Ms) : frameGen.Merge(series.Times, series.Ms);
        var stutters = Detectors.FindStutters(stutterTimes, stutterMs);
        var freezes = Detectors.FindFreezes(series.Times, series.Ms, FreezeThresholdMs);
        var bottleneck = BottleneckAnalyzer.Analyze(run);
        var context = FreezeContextBuilder.Build(run, freezes);
        var extra = ExtraSignals.Compute(run, double.TryParse(system.Get("display_current_hz"), NumberStyles.Float, CultureInfo.InvariantCulture, out var nowHz) ? nowHz : null);
        var trends = RunTrends.Compute(run, stutters, freezes);

        var facts = BuildFacts(run, capture.Info, metrics, stutters, freezes, bottleneck, context, extra, settings, system, background, shareBackgroundNames, trends, frameGen, CaptureQuality.Assess(capture.Info, run, metrics, bottleneck), gameFacts);
        var findings = Rules.Evaluate(metrics, freezes, stutters, bottleneck, context, extra, settings, system, background, facts, capture.Info, trends, frameGen, run);
        var specs = PcSpecs.Build(capture.Info, run, extra, bottleneck, system);

        return new AnalysisResult(
            capture.Info, runIndex, run, metrics, stutters, freezes, bottleneck, context, extra, settings, system,
            background, specs, findings, Rules.BuildNextSteps(findings), facts);
    }

    private static Dictionary<string, string> BuildFacts(
        CaptureRun run,
        CaptureInfo info,
        FrameMetrics m,
        IReadOnlyList<Stutter> stutters,
        IReadOnlyList<Freeze> freezes,
        BottleneckResult b,
        FreezeContext? ctx,
        ExtraSignals x,
        GameSettings? settings,
        SystemContext system,
        BackgroundScanResult? background,
        bool shareBackgroundNames,
        RunTrends trends,
        FrameGenerationSignal frameGen,
        IReadOnlyList<QualityIssue> quality,
        IReadOnlyList<ProfileItem>? gameFacts)
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
        Put("pc.resizable_bar", info.ResizableBar);
        Put("pc.hags", info.Hags);
        Put("pc.game_mode", info.GameMode);
        var memory = MemoryConfig.Parse(info.SystemRam);
        PutNum("pc.memory_modules", memory.Modules, "0");
        PutNum("pc.memory_mts", memory.SpeedMTs, "0");
        PutNum("cpu.limited_pct", trends.CpuLimitedPct, "0");
        if (gameFacts is { Count: > 0 })
        {
            // What the game can do, from a community wiki the user chose to look up. It says which options exist, not which are on.
            foreach (var fact in gameFacts)
            {
                Put("game." + fact.Key, fact.Value);
            }

            Put("game.info_source", "PCGamingWiki (community-written; says what the game offers, not what is switched on)");
        }

        if (quality.Count > 0)
        {
            Put("capture.quality_issues", CaptureQuality.ForFacts(quality));
        }
        Put("framegen.detected", frameGen.Active ? "yes" : "no");
        if (frameGen.Active)
        {
            PutNum("framegen.display_to_present_ratio", frameGen.DisplayToPresentRatio, "0.0");
            PutNum("framegen.generated_frames_pct", frameGen.GeneratedFramesPct, "0");
            PutNum("framegen.rendered_fps", frameGen.RenderedFps, "0.#");
            PutNum("framegen.displayed_fps", frameGen.DisplayedFps, "0.#");
            Put("stutter.method", "judged on the frames the screen shows, because frame generation fills the present stream with generated frames");
        }

        PutNum("trend.fps_first_quarter", trends.FpsFirstQuarter);
        PutNum("trend.fps_last_quarter", trends.FpsLastQuarter);
        PutNum("trend.events_first_third", trends.DurationSec >= 60 ? trends.EventsFirstThird : null, "0");
        PutNum("trend.events_last_third", trends.DurationSec >= 60 ? trends.EventsLastThird : null, "0");
        PutNum("trend.vram_first_gb", trends.VramFirstQuarterGb);
        PutNum("trend.vram_last_gb", trends.VramLastQuarterGb);
        PutNum("trend.ram_first_pct", trends.RamFirstQuarterPct, "0");
        PutNum("trend.ram_last_pct", trends.RamLastQuarterPct, "0");
        PutNum("trend.gpu_temp_first_c", trends.GpuTempFirstQuarterC, "0");
        PutNum("trend.gpu_temp_last_c", trends.GpuTempLastQuarterC, "0");
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
            PutNum("freeze.max_ms", freezes.Max(fr => fr.FrameMs), "0");
            Put("freeze.times_sec", string.Join(", ", freezes.Take(12).Select(fr => fr.TimeSec.ToString("0", CultureInfo.InvariantCulture))));
        }

        if (HitchPattern.Find(freezes, stutters) is { } pattern)
        {
            PutNum("hitch.period_sec", pattern.PeriodSec, "0.#");
            PutNum("hitch.periodic_events", pattern.Events, "0");
        }

        var cpuModel = CpuProfile.Parse(info.Cpu);
        if (CcdLoad.Compute(run, cpuModel) is { } ccd)
        {
            PutNum("cpu.ccd0_load_avg", ccd.FirstPct, "0");
            PutNum("cpu.ccd1_load_avg", ccd.SecondPct, "0");
        }

        if (run.FindSensor("CPU Max", "Clock") is { } cpuClock && Stats.Summarize(cpuClock.Values) is { Count: > 0 } cpuClockStats)
        {
            PutNum("cpu.clock_peak_mhz", cpuClockStats.Max, "0");
            PutNum("cpu.rated_boost_mhz", cpuModel.RatedBoostMhz, "0");
        }

        if (run.FindSensor("GPU Core", "Clock") is { } gpuClock)
        {
            var clocks = gpuClock.Values.Where(v => double.IsFinite(v) && v > 0).Order().ToArray();
            if (clocks.Length >= 20)
            {
                PutNum("gpu.clock_p10_mhz", Stats.Percentile(clocks, 0.10), "0");
                PutNum("gpu.clock_p95_mhz", Stats.Percentile(clocks, 0.95), "0");
            }
        }

        Put("bottleneck.verdict", b.Verdict.ToString());
        Put("bottleneck.confidence", b.Confidence.ToString());
        PutNum("gpu.busy_pct", b.GpuBusyPct);
        PutNum("gpu.load_avg", b.GpuLoadAvg);
        PutNum("cpu.busiest_thread_load_avg", b.CpuMaxThreadLoadAvg);
        PutNum("cpu.total_load_avg", b.CpuTotalLoadAvg);
        PutNum("gpu.temp_max_c", b.GpuTempMax);
        PutNum("gpu.hotspot_max_c", b.GpuHotSpotMax);
        PutNum("gpu.mem_junction_max_c", x.MemJunctionMaxC);
        PutNum("cpu.temp_max_c", b.CpuTempMax);
        PutNum("gpu.power_limit_hits_pct", b.PowerLimitHitsPct);
        PutNum("vram.used_max_gb", b.VramUsedMaxGb);
        PutNum("vram.total_gb", b.VramTotalGb);
        if (VramFull.Compute(run) is { } vramFull)
        {
            PutNum("vram.near_full_pct", vramFull.SharePct, "0");
        }

        PutNum("display.refresh_max_hz", x.RefreshMaxHz);
        PutNum("display.refresh_min_hz", x.RefreshMinHz);
        if (x.RefreshMaxHz is not null)
        {
            Put("display.vrr_evidence", x.VrrEvidence ? "yes" : "no");
        }

        PutNum("display.frames_above_refresh_pct", x.FramesAboveRefreshPct);
        PutNum("present.tearing_allowed_pct", x.TearingAllowedPct, "0");
        PutNum("present.sync_interval_zero_pct", x.SyncZeroPct, "0");
        PutNum("present.vsync_on_grid_pct", x.SyncQuantizedPct, "0");
        PutNum("latency.avg_ms", x.LatencyAvgMs);
        PutNum("latency.p95_ms", x.LatencyP95Ms);
        PutNum("latency.max_ms", x.LatencyMaxMs, "0");
        PutNum("pacing.anim_error_avg_ms", x.AnimErrorAvgMs);
        PutNum("pacing.anim_error_p99_ms", x.AnimErrorP99Ms);
        PutNum("memory.ram_usage_avg_pct", x.RamUsageAvgPct);
        PutNum("memory.ram_usage_max_pct", x.RamUsageMaxPct);
        PutNum("memory.virtual_usage_avg_pct", x.VirtualMemAvgPct);
        PutNum("memory.virtual_usage_max_pct", x.VirtualMemMaxPct);

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

        // The user's own words and read-only Windows facts. Hardware model names of drives and monitors stay local.
        foreach (var item in system.Items)
        {
            if (item.Key is "game_drive.model" or "setup.monitor_model" or "game_install_path_known")
            {
                continue;
            }

            Put((item.Source == SettingSource.User ? "" : "system.") + item.Key, item.Value);
        }

        if (background is not null)
        {
            Put("background.checked", "yes");
            PutNum("background.duration_sec", background.DurationSec, "0");
            PutNum("background.other_cpu_pct", background.OtherCpuPct);
            PutNum("background.other_disk_mbps", background.OtherDiskMBps);
            PutNum("background.other_gpu_pct", background.OtherGpuPct);
            PutNum("background.game_cpu_pct", background.GameCpuPct);
            PutNum("background.known_apps_running", background.KnownInterferers.Count(), "0");
            Put("background.known_categories", background.CategorySummary);
            if (shareBackgroundNames)
            {
                Put("background.top_apps", string.Join(", ", background.Others.Take(5).Select(p => p.Name)));
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
