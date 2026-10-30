using System.Globalization;
using Daignostic.Core.Capture;
using Daignostic.Core.Config;
using Daignostic.Core.SystemInfo;

namespace Daignostic.Core.Analysis;

/// <summary>
/// Diagnoses that need more than one signal: how the CPU limits the game, what changes over a long session, what
/// the memory is set up as, and the Windows and driver options that commonly cost smoothness. Each one states what
/// was measured, why it matters and what to try; the ones that are only likely, not proven, say so.
/// </summary>
internal static class DeepRules
{
    public static void Evaluate(
        Rules.AddFinding add,
        FrameMetrics metrics,
        IReadOnlyList<Freeze> freezes,
        IReadOnlyList<Stutter> stutters,
        BottleneckResult bottleneck,
        RunTrends trends,
        CaptureInfo info,
        SystemContext system,
        GameSettings? settings,
        ExtraSignals extra)
    {
        var hitches = freezes.Count > 0 || stutters.Count > 20 || metrics.Low1AverageFps < metrics.AverageFps * 0.6;
        var cpuBound = bottleneck.Verdict == BottleneckVerdict.CpuBound;

        CpuLimits(add, bottleneck, trends, metrics, cpuBound);
        OverTime(add, trends, hitches);
        Memory(add, info, cpuBound, hitches);
        WindowsAndDriver(add, info, system, hitches, cpuBound);
        GameOptions(add, settings, metrics, bottleneck, extra, hitches);
    }

    private static void CpuLimits(Rules.AddFinding add, BottleneckResult b, RunTrends t, FrameMetrics m, bool cpuBound)
    {
        // One busy thread with an idle rest means the game cannot spread the work: more cores will not help.
        if (cpuBound && b.CpuTotalLoadAvg is { } total && b.CpuMaxThreadLoadAvg is { } busiest && total <= 60 && busiest >= 85)
        {
            add("CPU_SINGLE_THREAD", m.AverageFps < 60 ? Severity.Medium : Severity.Low,
                "One CPU thread is the limit",
                $"One CPU thread ran at {F(busiest)}% on average while the whole CPU averaged only {F(total)}%. The game puts most of its work on one thread, so the frame rate is capped by how fast a single core is, not by how many cores you have.",
                ["cpu.busiest_thread_load_avg", "cpu.total_load_avg", "gpu.load_avg"],
                [
                    "Lower the settings that load that thread: crowd and NPC density, view distance, physics and simulation quality.",
                    "Faster memory helps CPU-limited games: make sure the RAM runs at its rated speed (XMP/EXPO in the BIOS).",
                    "A CPU with faster single-core speed or more cache (for example an X3D model) helps this kind of game; more cores alone will not.",
                ]);
        }

        // Even a GPU-bound run can have stretches where the CPU held the GPU back (busy areas, loading).
        if (!cpuBound && t.CpuLimitedPct is >= 15 and var share)
        {
            add("CPU_LIMITED_MOMENTS", share >= 30 ? Severity.Medium : Severity.Low,
                "Parts of the run were limited by the CPU",
                $"In {F(share)}% of the capture one CPU thread was above 90% while the GPU was below 85%, so the GPU was waiting for the CPU. That is common in busy areas such as towns, crowds and big battles.",
                ["cpu.limited_pct", "cpu.busiest_thread_load_avg", "gpu.load_avg"],
                [
                    "If those moments are the busy parts of the game, lower the CPU-heavy settings (crowd density, view distance, shadows, physics).",
                    "Check the background programs result and the memory speed finding: both take CPU time from the game.",
                ]);
        }
    }

    private static void OverTime(Rules.AddFinding add, RunTrends t, bool hitches)
    {
        if (t.FpsFirstQuarter is { } first && t.FpsLastQuarter is { } last && first >= 20 && last < first * 0.85)
        {
            var causes = new List<string>();
            var keys = new List<string> { "trend.fps_first_quarter", "trend.fps_last_quarter" };
            if (t.VramFirstQuarterGb is { } v1 && t.VramLastQuarterGb is { } v2 && v2 > v1 * 1.15)
            {
                causes.Add($"video memory use grew from {F(v1)} to {F(v2)} GB");
                keys.AddRange(["trend.vram_first_gb", "trend.vram_last_gb"]);
            }

            if (t.RamFirstQuarterPct is { } r1 && t.RamLastQuarterPct is { } r2 && r2 > r1 + 8)
            {
                causes.Add($"system memory use grew from {F(r1)}% to {F(r2)}%");
                keys.AddRange(["trend.ram_first_pct", "trend.ram_last_pct"]);
            }

            if (t.GpuTempFirstQuarterC is { } c1 && t.GpuTempLastQuarterC is { } c2 && c2 > c1 + 8)
            {
                causes.Add($"the GPU warmed up from {F(c1)} to {F(c2)} °C");
                keys.AddRange(["trend.gpu_temp_first_c", "trend.gpu_temp_last_c"]);
            }

            var why = causes.Count > 0
                ? "At the same time " + string.Join(" and ", causes) + ", which fits."
                : "Nothing in the sensors explains it, so a memory leak in the game or a background program starting up are still possible.";
            add("PERFORMANCE_DECLINES", Severity.Medium,
                "Performance gets worse the longer the run goes on",
                $"The frame rate was {F(first)} fps in the first quarter and {F(last)} fps in the last quarter. {why}",
                [.. keys],
                [
                    "If video memory grew, lower texture quality or resolution scale so it stays below your card's total.",
                    "If system memory grew, restart the game now and then and close other programs; some games leak memory over long sessions.",
                    "If the GPU warmed up, clean the dust from the PC and improve the airflow; a lower power or frame cap also helps.",
                ]);
        }

        // Hitches that fade as the run goes on are typical of shader compilation and loading new areas.
        if (hitches && t.EventsFirstThird >= 8 && t.EventsFirstThird >= 2.5 * Math.Max(1, t.EventsLastThird))
        {
            add("HITCHES_FADE", Severity.Low,
                "Most of the hitches happen early in the run",
                $"{t.EventsFirstThird} stutters and freezes fell in the first third of the capture and {t.EventsLastThird} in the last third. That pattern usually means the game was compiling shaders or loading new areas, which gets better once they are cached. It is a likely explanation, not a proven one.",
                ["trend.events_first_third", "trend.events_last_third", "stutter.count", "freeze.count"],
                [
                    "Play a little before judging: record the same route again. If the second run is smoother, it was shader or asset caching.",
                    "Keep the driver shader cache on and large, and install the game on an SSD.",
                    "If the game has a 'precompile shaders' option, run it after each driver update.",
                ]);
        }
    }

    private static void Memory(Rules.AddFinding add, CaptureInfo info, bool cpuBound, bool hitches)
    {
        var mem = MemoryConfig.Parse(info.SystemRam);
        var matters = cpuBound || hitches;

        if (mem.Modules == 1 && matters)
        {
            add("RAM_SINGLE_MODULE", cpuBound ? Severity.Medium : Severity.Low,
                "Only one memory stick is installed",
                "With a single stick the memory runs in single-channel mode, which halves its bandwidth on most PCs. CPU-heavy games and the lowest frame rates are the first to suffer. How much depends on the game and on the memory type.",
                ["pc.memory_modules", "capture.system_ram"],
                [
                    "Add a second identical stick (or replace the one with a matching pair) so the memory runs in dual-channel mode.",
                    "Put the sticks in the slots your motherboard manual names for dual-channel.",
                ]);
        }

        if (mem.SpeedMTs is { } speed && matters && (speed <= 2667 || (speed == 4800 && cpuBound)))
        {
            add("RAM_SLOW_SPEED", cpuBound ? Severity.Medium : Severity.Low,
                $"The memory runs at {speed} MT/s, which may be its default speed",
                "Memory kits usually run at a slow default speed until you switch on their rated profile (XMP on Intel, EXPO or DOCP on AMD) in the BIOS. This is one of the cheapest fixes for CPU-limited games and for poor 1% lows. If your kit is only rated for this speed, nothing is wrong.",
                ["pc.memory_mts", "capture.system_ram"],
                [
                    "Check the speed printed on your memory kit. If it is higher, enable XMP, EXPO or DOCP in the BIOS.",
                    "If the PC will not start after that, load the default BIOS settings and try a lower profile.",
                ]);
        }
    }

    private static void WindowsAndDriver(Rules.AddFinding add, CaptureInfo info, SystemContext system, bool hitches, bool cpuBound)
    {
        if (info.GameMode.Equals("Disabled", StringComparison.OrdinalIgnoreCase) && hitches)
        {
            add("GAME_MODE_OFF", Severity.Low, "Windows Game Mode is off",
                "Game Mode asks Windows to keep background tasks from taking CPU time from the game. It rarely changes much, but it is free to switch on.",
                ["pc.game_mode"],
                ["Turn Game Mode on in Windows Settings > Gaming > Game Mode, then record again."]);
        }

        if (info.ResizableBar.Equals("Disabled", StringComparison.OrdinalIgnoreCase) && (cpuBound || hitches))
        {
            add("RESIZABLE_BAR_OFF", Severity.Low, "Resizable BAR is off",
                "Resizable BAR lets the CPU reach all of the graphics card's memory at once. Many recent games gain a little from it, and it is free where your hardware supports it. It is not supported on every PC.",
                ["pc.resizable_bar"],
                [
                    "If your motherboard and graphics card support it, enable 'Above 4G decoding' and 'Resizable BAR' in the BIOS.",
                    "Check your graphics card maker's page for a supported list first.",
                ]);
        }

        if ((info.PresentationMode.Contains("Composed", StringComparison.OrdinalIgnoreCase) ||
             info.PresentationMode.Equals("Windowed", StringComparison.OrdinalIgnoreCase)) && hitches)
        {
            add("COMPOSED_PRESENT", Severity.Low, "The game is drawn through the Windows desktop compositor",
                $"The capture says the game presented as \"{info.PresentationMode}\". In that mode every frame is copied through the desktop, which adds latency and can cost smoothness, and it disables some tearing-free display modes.",
                ["capture.presentation_mode"],
                [
                    "Use exclusive fullscreen or the borderless mode of the game, not a window.",
                    "Turn off overlays that hook into the game, then record again.",
                ]);
        }

        if (hitches && system.Is("game_dvr", "On"))
        {
            add("BACKGROUND_CAPTURE_ON", Severity.Low, "Windows background game recording is on",
                "The Xbox Game Bar can record gameplay in the background, which costs some GPU and disk time even when you never save a clip.",
                ["system.game_dvr"],
                ["Turn off background recording in Windows Settings > Gaming > Captures, if you do not use it."]);
        }

        if ((cpuBound || hitches) && system.Is("memory_integrity", "On"))
        {
            add("MEMORY_INTEGRITY_ON", Severity.Info, "Windows Memory integrity is on",
                "Memory integrity (virtualization-based security) protects Windows from some attacks and can cost a few percent in CPU-limited games. Many people keep it on for the protection; this is only mentioned because your game is CPU-limited or hitching.",
                ["system.memory_integrity"],
                []);
        }
    }

    /// <summary>What the game's own settings add to the picture. Only settings that were read for this capture get here.</summary>
    private static void GameOptions(Rules.AddFinding add, GameSettings? settings, FrameMetrics m, BottleneckResult b, ExtraSignals x, bool hitches)
    {
        if (settings is null)
        {
            return;
        }

        var gpuBound = b.Verdict == BottleneckVerdict.GpuBound;

        if (settings.Get("dlss") == "Off" && gpuBound && m.AverageFps < 60)
        {
            add("UPSCALING_OFF_GPU_BOUND", Severity.Medium,
                "Upscaling is off while the GPU is the limit",
                $"DLSS is off in the game's settings and the GPU is the limit at {F(m.AverageFps)} fps. Upscaling renders fewer pixels and is usually the biggest single gain for a GPU-limited game, at a small cost in sharpness.",
                ["settings.dlss", "bottleneck.verdict", "fps.average"],
                [
                    "Turn DLSS on (Quality first, then Balanced if you need more), then record the same route again and compare.",
                    "If the game offers FSR or XeSS and your card does not support DLSS, use one of those.",
                ]);
        }

        if (settings.Get("frame_generation") is { } fg && fg.StartsWith("On", StringComparison.OrdinalIgnoreCase) &&
            (hitches || (x.AnimErrorP99Ms ?? 0) >= 8 || (x.LatencyAvgMs ?? 0) >= 50))
        {
            add("FRAME_GENERATION_ON", Severity.Low,
                "Frame generation is on while motion or latency is not right",
                "Frame generation inserts extra frames to raise the displayed frame rate. It can add latency and uneven pacing, and the frame rate in a capture may count generated frames. It also needs a healthy base frame rate to look good. This is a hypothesis to test, not a proven cause.",
                ["settings.frame_generation", "pacing.anim_error_p99_ms", "latency.avg_ms", "stutter.count"],
                [
                    "Turn frame generation off, record the same route again and compare smoothness and latency.",
                    "If you keep it, make sure the frame rate without it is already comfortable (about 60 fps or more).",
                ]);
        }

        if (settings.Get("low_latency") == "Off" && gpuBound && (x.LatencyAvgMs ?? 0) >= 40)
        {
            add("REFLEX_OFF", Severity.Low,
                "Low latency mode is off while the GPU is the limit",
                $"Input latency averages {F(x.LatencyAvgMs ?? 0)} ms and the GPU is the limit. A GPU-limited game queues frames, which adds delay; NVIDIA Reflex (or the equivalent low latency mode) keeps that queue short.",
                ["settings.low_latency", "latency.avg_ms", "bottleneck.verdict"],
                ["Turn Reflex (low latency mode) on in the game's settings, then record again and compare latency."]);
        }
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
