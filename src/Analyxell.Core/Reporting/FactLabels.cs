using System.Globalization;

namespace Analyxell.Core.Reporting;

/// <summary>
/// Plain names for the measurement keys that findings and AI answers cite ("fps.low1_average" becomes "1% low"), so
/// nobody has to read internal identifiers. The keys themselves stay as they are for the AI and the tests.
/// </summary>
public static class FactLabels
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fps.average"] = "average fps",
        ["fps.p1"] = "1% (P1) fps",
        ["fps.low1_average"] = "1% low",
        ["fps.low0_1_average"] = "0.1% low",
        ["stutter.count"] = "stutter count",
        ["freeze.count"] = "freeze count",
        ["freeze.max_ms"] = "longest freeze",
        ["freeze.times_sec"] = "when the freezes happened",
        ["bottleneck.verdict"] = "what limits the game",
        ["gpu.load_avg"] = "GPU load",
        ["gpu.busy_pct"] = "GPU busy time",
        ["gpu.temp_max_c"] = "peak GPU temperature",
        ["gpu.hotspot_max_c"] = "peak GPU hot spot",
        ["gpu.mem_junction_max_c"] = "peak video memory temperature",
        ["gpu.power_limit_hits_pct"] = "time at the GPU power limit",
        ["cpu.busiest_thread_load_avg"] = "busiest CPU thread",
        ["cpu.total_load_avg"] = "CPU load",
        ["cpu.temp_max_c"] = "peak CPU temperature",
        ["cpu.limited_pct"] = "time the CPU was the limit",
        ["vram.used_max_gb"] = "peak video memory used",
        ["vram.total_gb"] = "video memory size",
        ["memory.ram_usage_avg_pct"] = "average memory use",
        ["memory.ram_usage_max_pct"] = "peak memory use",
        ["memory.virtual_usage_avg_pct"] = "average Windows memory commit",
        ["memory.virtual_usage_max_pct"] = "peak Windows memory commit",
        ["pc.memory_modules"] = "memory sticks",
        ["pc.memory_mts"] = "memory speed",
        ["pc.resizable_bar"] = "Resizable BAR",
        ["pc.game_mode"] = "Windows Game Mode",
        ["capture.system_ram"] = "installed memory",
        ["capture.presentation_mode"] = "how the game was presented",
        ["capture.quality_issues"] = "capture quality",
        ["display.refresh_max_hz"] = "display refresh rate",
        ["display.vrr_evidence"] = "variable refresh seen",
        ["display.frames_above_refresh_pct"] = "frames above the refresh rate",
        ["present.tearing_allowed_pct"] = "frames where tearing was allowed",
        ["present.sync_interval_zero_pct"] = "frames without VSync",
        ["latency.avg_ms"] = "average latency",
        ["latency.p95_ms"] = "95th percentile latency",
        ["pacing.anim_error_avg_ms"] = "average motion pacing error",
        ["pacing.anim_error_p99_ms"] = "worst motion pacing error",
        ["framegen.detected"] = "frame generation",
        ["framegen.rendered_fps"] = "rendered fps",
        ["framegen.displayed_fps"] = "displayed fps",
        ["framegen.display_to_present_ratio"] = "displayed to rendered frames",
        ["framegen.generated_frames_pct"] = "generated frames",
        ["trend.fps_first_quarter"] = "fps at the start",
        ["trend.fps_last_quarter"] = "fps at the end",
        ["trend.events_first_third"] = "hitches early in the run",
        ["trend.events_last_third"] = "hitches late in the run",
        ["trend.vram_first_gb"] = "video memory at the start",
        ["trend.vram_last_gb"] = "video memory at the end",
        ["trend.ram_first_pct"] = "memory use at the start",
        ["trend.ram_last_pct"] = "memory use at the end",
        ["trend.gpu_temp_first_c"] = "GPU temperature at the start",
        ["trend.gpu_temp_last_c"] = "GPU temperature at the end",
        ["system.power_plan"] = "Windows power plan",
        ["system.shader_cache_mb"] = "shader cache size",
        ["system.game_dvr"] = "Windows background recording",
        ["system.memory_integrity"] = "Windows Memory integrity",
        ["system.game_drive.media"] = "game drive type",
        ["system.game_drive.free_pct"] = "game drive free space",
        ["system.display_current_hz"] = "Windows display refresh rate",
        ["setup.vsync"] = "your VSync setting",
        ["setup.display_sync"] = "your display sync setting",
        ["setup.frame_cap"] = "your frame cap",
        ["setup.low_latency"] = "your low latency setting",
        ["setup.monitor_max_hz"] = "your monitor's refresh rate",
        ["settings.dlss"] = "game setting: DLSS",
        ["settings.frame_generation"] = "game setting: frame generation",
        ["settings.low_latency"] = "game setting: low latency",
        ["settings.ray_tracing"] = "game setting: ray tracing",
        ["background.other_cpu_pct"] = "other programs' CPU use",
        ["background.other_disk_mbps"] = "other programs' disk use",
        ["background.other_gpu_pct"] = "other programs' GPU use",
        ["background.known_categories"] = "known background programs",
    };

    /// <summary>A readable name for one key. Unknown keys get their words pulled apart rather than shown raw.</summary>
    public static string For(string key)
    {
        if (key.StartsWith("finding:", StringComparison.OrdinalIgnoreCase))
        {
            return "the finding \"" + key[8..].Replace('_', ' ').ToLowerInvariant() + "\"";
        }

        if (key.StartsWith("freeze_ctx.", StringComparison.OrdinalIgnoreCase))
        {
            return "what the sensors showed around the freezes";
        }

        return Known.TryGetValue(key, out var label) ? label : Humanize(key);
    }

    /// <summary>A comma-separated list of readable names, without repeats.</summary>
    public static string List(IEnumerable<string> keys) =>
        string.Join(", ", keys.Select(For).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string Humanize(string key)
    {
        var text = key.Replace('.', ' ').Replace('_', ' ').Trim();
        return text.Length == 0 ? key : text[..1].ToLower(CultureInfo.InvariantCulture) + text[1..];
    }
}
