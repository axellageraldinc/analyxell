using System.Globalization;
using Analyxell.Core.Background;
using Analyxell.Core.Capture;
using Analyxell.Core.Config;
using Analyxell.Core.SystemInfo;

namespace Analyxell.Core.Analysis;

/// <summary>
/// Deterministic diagnostic rules. Each finding cites the fact keys it is based on and lists things the user can
/// try. Suggestions are limited to in-game settings and simple, reversible checks; they are meant to be tried one at
/// a time and verified by recording again and comparing.
/// </summary>
internal static class Rules
{
    private const string BackgroundCaveat = "This check reflects the moment it was run, which may differ from the captured session.";
    private const string OneAtATime = "Change one thing, record the same route again, and use Compare to see whether it helped.";

    public static IReadOnlyList<Finding> Evaluate(
        FrameMetrics metrics,
        IReadOnlyList<Freeze> freezes,
        IReadOnlyList<Stutter> stutters,
        BottleneckResult bottleneck,
        FreezeContext? ctx,
        ExtraSignals extra,
        GameSettings? settings,
        SystemContext system,
        BackgroundScanResult? background,
        IReadOnlyDictionary<string, string> facts,
        CaptureInfo info,
        RunTrends trends,
        FrameGenerationSignal frameGen)
    {
        var list = new List<Finding>();

        string[] Ev(params string[] keys) => keys.Where(facts.ContainsKey).ToArray();
        void Add(string id, Severity severity, string title, string detail, string[] keys, string[] suggestions, bool localOnly = false)
        {
            var evidence = Ev(keys);
            if (evidence.Length == 0)
            {
                evidence = Ev("fps.average");
            }

            list.Add(new Finding(id, severity, title, detail, evidence, suggestions, localOnly));
        }

        var verdict = bottleneck.Verdict;
        var vrr = extra.VrrEvidence || UserSaysVrr(system);

        // 1. Freezes and where they come from.
        if (freezes.Count > 0)
        {
            var worst = freezes.Max(f => f.FrameMs);
            var severity = freezes.Count >= 10 || worst >= 500 ? Severity.High
                : freezes.Count >= 3 ? Severity.Medium
                : Severity.Low;
            Add("FREEZES", severity,
                $"{freezes.Count} freeze(s) of {CaptureAnalyzer.FreezeThresholdMs:0} ms or more",
                $"The worst freeze lasted {worst:0} ms. Freezes feel like the game hanging and hurt 0.1% lows far more than the average.",
                ["freeze.count", "freeze.max_ms", "freeze.times_sec"],
                [
                    "Note what you were doing at the freeze times (loading an area, a cutscene, a fight) so you can look for a pattern.",
                    "Play the same route again. Freezes in the same place point to the game itself (streaming or shader compilation); freezes in different places point to something else on the PC.",
                ]);
        }

        if (freezes.Count > 0 && ctx?.Find("GPU load") is { } gpu)
        {
            if (gpu.AtFreezeAverage >= 85)
            {
                Add("FREEZES_GPU_BUSY", Severity.Medium,
                    "The GPU was busy during the freezes",
                    $"GPU load averaged {F(gpu.AtFreezeAverage)}% around the freezes. That points to GPU-side or engine-side work " +
                    "(for example ray tracing, shader or asset work), not to a system-wide stall.",
                    ["freeze_ctx.gpu_load.at_freeze_avg", "freeze_ctx.gpu_load.overall_avg"],
                    [
                        "Lower the most GPU-heavy settings one step at a time: ray tracing first, then shadows, reflections and global illumination.",
                        "Try a stronger upscaling mode (for example DLSS or FSR from Quality to Balanced) to cut GPU work.",
                        OneAtATime,
                    ]);
            }
            else if (gpu.AtFreezeAverage < 50)
            {
                Add("FREEZES_GPU_IDLE", Severity.Medium,
                    "The GPU was mostly idle during the freezes",
                    $"GPU load averaged only {F(gpu.AtFreezeAverage)}% around the freezes, so the GPU was waiting. " +
                    "Look at the CPU, disk, memory or shader compilation.",
                    ["freeze_ctx.gpu_load.at_freeze_avg", "freeze_ctx.gpu_load.overall_avg"],
                    [
                        "Run the background check below to see what else is using the CPU or disk.",
                        "Play the same area twice: if it is smooth the second time, the first pass was compiling shaders or loading assets.",
                        "If the game is on a hard disk, move it to an SSD.",
                    ]);
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
                    $"{FriendlyDrive(busy.Label)} reached {F(busy.AtFreezeMax)}% near a freeze. Asset streaming from a slow or busy drive can cause this. {GameDriveNote(system, busy.Label)}",
                    ["freeze_ctx." + CaptureAnalyzer.Slug(busy.Label) + ".at_freeze_max"],
                    [
                        "Install the game on an SSD, an NVMe drive if you can.",
                        "Pause downloads, game updates and cloud sync while you play.",
                        "Keep at least 15 to 20% of the drive free.",
                    ]);
            }
            else if (drives.Count > 0)
            {
                Add("DISK_RULED_OUT", Severity.Info,
                    "Disk activity does not explain the freezes",
                    $"No drive exceeded {F(drives.Max(d => d.AtFreezeMax))}% activity near a freeze.",
                    drives.Select(d => "freeze_ctx." + CaptureAnalyzer.Slug(d.Label) + ".at_freeze_max").ToArray(),
                    []);
            }
        }

        // 2. Frame rate and consistency.
        if (metrics.AverageFps > 0 && metrics.AverageFps < 60)
        {
            var capSet = double.TryParse(system.Get("setup.frame_cap"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cap) && cap < 60;
            Add("LOW_FPS", capSet ? Severity.Info : metrics.AverageFps < 30 ? Severity.High : Severity.Medium,
                $"Average frame rate is below 60 fps ({F(metrics.AverageFps)} fps)",
                capSet ? $"You said a frame cap of {F(cap)} fps is set, so this may be intended." : "Below 60 fps most games look and feel noticeably choppy.",
                ["fps.average", "bottleneck.verdict"],
                verdict == BottleneckVerdict.CpuBound
                    ?
                    [
                        "Lower CPU-heavy settings: view distance, crowd and NPC density, simulation quality, shadow distance.",
                        "Close background programs (run the background check below).",
                        "Raising resolution or GPU-heavy settings costs little frame rate here, because the GPU has spare capacity.",
                    ]
                    :
                    [
                        "Turn on upscaling (DLSS or FSR) or lower the render resolution scale.",
                        "Lower the most GPU-heavy settings one step: ray tracing, shadows, reflections, global illumination, volumetric effects.",
                        "Check the game is using the right GPU and the display mode you expect.",
                        OneAtATime,
                    ]);
        }

        if (metrics.AverageFps > 0 && metrics.Low1AverageFps < metrics.AverageFps * 0.5)
        {
            Add("UNEVEN_FRAMETIMES", Severity.Medium,
                "1% lows are far below the average",
                $"The average is {F(metrics.AverageFps)} fps but the 1% low average is {F(metrics.Low1AverageFps)} fps, " +
                "so the game feels worse than the average suggests.",
                ["fps.average", "fps.low1_average", "fps.low0_1_average"],
                [
                    "Deal with the freezes and spikes listed here first: they are what drags the lows down.",
                    "Cap the frame rate a little below what your PC can sustain. A steady, slightly lower frame rate feels smoother than a high, uneven one.",
                    "If your monitor supports variable refresh (G-SYNC or FreeSync), turn it on.",
                ]);
        }

        // 3. Hardware limits and heat.
        switch (verdict)
        {
            case BottleneckVerdict.ThermalThrottling:
                Add("THERMAL", Severity.High, "Thermal throttling", bottleneck.Reasoning, ["gpu.temp_max_c", "gpu.hotspot_max_c"],
                [
                    "Check that the case fans and GPU fans spin up, and clear dust from filters, heatsinks and fans.",
                    "Improve airflow, and keep the PC away from heat sources.",
                    "Cap the frame rate or lower settings to reduce heat while you investigate.",
                ]);
                break;
            case BottleneckVerdict.PowerLimited:
                Add("POWER_LIMIT", Severity.Medium, "GPU power limit reached", bottleneck.Reasoning, ["gpu.power_limit_hits_pct"],
                [
                    "If you or your GPU software lowered the power limit, raise it back to default.",
                    "A frame cap or lower settings reduces power draw if the limit is intentional.",
                ]);
                break;
            case BottleneckVerdict.GpuBound:
                Add("GPU_BOUND", Severity.Info, "GPU-bound", bottleneck.Reasoning, ["bottleneck.verdict", "gpu.load_avg", "gpu.busy_pct"],
                [
                    "To gain frame rate: lower resolution scale, use upscaling, or lower GPU-heavy settings.",
                    "To gain smoothness at the same frame rate: cap the frame rate slightly below what the GPU can do, which also lowers latency.",
                ]);
                break;
            case BottleneckVerdict.CpuBound:
                Add("CPU_BOUND", metrics.AverageFps < 60 ? Severity.Medium : Severity.Info, "The CPU is holding the GPU back",
                    bottleneck.Reasoning + " The graphics card is waiting for the processor, so lowering resolution or GPU-heavy settings will barely raise the frame rate.",
                    ["bottleneck.verdict", "cpu.busiest_thread_load_avg", "gpu.load_avg"],
                [
                    "Lower CPU-heavy settings: view distance, crowd and NPC density, shadow distance, physics.",
                    "Check that the memory runs at its rated speed (XMP/EXPO in the BIOS) and in dual-channel mode.",
                    "Close background programs and check the background results.",
                    "To test it: lower the resolution. If the frame rate hardly changes, the CPU is the limit.",
                ]);
                break;
            case BottleneckVerdict.Balanced:
                Add("BALANCED", Severity.Info, "No single bottleneck", bottleneck.Reasoning, ["bottleneck.verdict"], []);
                break;
        }

        const string CoolingNote = "Typical safe limits vary by model; check your hardware's specification.";
        if (bottleneck.GpuTempMax is { } gt && gt >= 85)
        {
            Add("GPU_HOT", gt >= 90 ? Severity.High : Severity.Medium, $"The GPU gets hot (up to {F(gt)} °C)",
                $"{CoolingNote} High temperatures make the GPU slow itself down.", ["gpu.temp_max_c", "gpu.hotspot_max_c"],
                CoolingSuggestions());
        }
        else if (bottleneck.GpuHotSpotMax is { } hs && hs >= 95)
        {
            Add("GPU_HOT", Severity.Medium, $"The GPU hot spot is high (up to {F(hs)} °C)", CoolingNote, ["gpu.hotspot_max_c", "gpu.temp_max_c"], CoolingSuggestions());
        }

        if (bottleneck.CpuTempMax is { } ct && ct >= 90)
        {
            Add("CPU_HOT", ct >= 95 ? Severity.High : Severity.Medium, $"The CPU gets hot (up to {F(ct)} °C)", CoolingNote, ["cpu.temp_max_c"], CoolingSuggestions());
        }

        if (extra.MemJunctionMaxC is { } mj && mj >= 95)
        {
            Add("VRAM_HOT", Severity.Medium, $"Video memory runs hot (up to {F(mj)} °C)", CoolingNote, ["gpu.mem_junction_max_c"], CoolingSuggestions());
        }

        if (bottleneck.VramUsedMaxGb is { } used && bottleneck.VramTotalGb is { } total && total > 0 && used / total >= 0.85)
        {
            var ratio = used / total;
            Add("VRAM_PRESSURE", ratio >= 0.95 ? Severity.High : Severity.Medium,
                "Video memory is nearly full",
                $"Peak VRAM use was {F(used)} of {F(total)} GB ({ratio * 100:0}%). Running out of VRAM causes hitching.",
                ["vram.used_max_gb", "vram.total_gb"],
                [
                    "Lower texture quality one step, which is the biggest VRAM saver.",
                    "Lower the render resolution scale or use upscaling.",
                    "Close programs that use video memory (browsers, other games, recorders).",
                ]);
        }

        if (extra.RamUsageMaxPct is { } rm && rm >= 92)
        {
            Add("RAM_PRESSURE", Severity.Medium, "System memory was almost full",
                $"RAM use peaked at {F(rm)}%. When memory runs out Windows pages to disk and the game stutters.",
                ["memory.ram_usage_max_pct", "memory.ram_usage_avg_pct"],
                [
                    "Close memory-hungry programs while you play, especially browsers with many tabs.",
                    "If it is regularly this high, more RAM is the fix.",
                ]);
        }
        else if (extra.VirtualMemAvgPct is { } vm && vm >= 90)
        {
            Add("COMMIT_PRESSURE", Severity.Low, "Windows memory commit is nearly full",
                $"Virtual memory use averaged {F(vm)}%. It is close to the limit of RAM plus page file.",
                ["memory.virtual_usage_avg_pct", "memory.virtual_usage_max_pct"],
                [
                    "Close programs you do not need while gaming.",
                    "Leave Windows to manage the page file size automatically.",
                ]);
        }

        // 4. Display sync, tearing, latency and pacing.
        EvaluateDisplay(Add, metrics, extra, system, vrr, settings);

        if (extra.LatencyAvgMs is { } la && (la >= 60 || extra.LatencyP95Ms >= 100))
        {
            var reflexOff = system.Is("setup.low_latency", "Off");
            Add("HIGH_LATENCY", la >= 60 ? Severity.Medium : Severity.Low,
                $"Input-to-display latency is high (average {F(la)} ms)",
                "This estimate is how long it takes from an input until the result is on screen. High values feel sluggish in fast games."
                + (reflexOff ? " You said Reflex or low-latency mode is off." : ""),
                ["latency.avg_ms", "latency.p95_ms"],
                [
                    "Turn on NVIDIA Reflex (or AMD Anti-Lag) in the game or driver if available.",
                    verdict == BottleneckVerdict.GpuBound
                        ? "Cap the frame rate slightly below what the GPU can do. A GPU running flat out adds latency."
                        : "Cap the frame rate to a steady value your PC can hold.",
                    "Turn off VSync if you use variable refresh, or use a frame cap with it.",
                ]);
        }

        if (extra.AnimErrorP99Ms is { } ae && ae >= 10)
        {
            Add("UNEVEN_PACING", Severity.Low, "Motion pacing is uneven",
                $"Frames were sometimes shown about {F(ae)} ms away from where smooth motion needs them. Motion can look slightly juddery even when the frame rate is fine.",
                ["pacing.anim_error_p99_ms", "pacing.anim_error_avg_ms"],
                [
                    "Use a steady frame cap, ideally just below the monitor's refresh rate.",
                    "Enable variable refresh (G-SYNC or FreeSync) if your monitor supports it.",
                ]);
        }

        // 5. Settings the user told us about, and Windows facts.
        EvaluateSystem(Add, system, extra, freezes.Count, stutters.Count, metrics);

        if (settings?.IsOn("ray_tracing") == true && (freezes.Count >= 3 || metrics.Low1AverageFps < metrics.AverageFps * 0.5))
        {
            Add("RAY_TRACING_WITH_HITCHES", Severity.Medium,
                "Ray tracing is on while the game hitches",
                "This is a hypothesis to test, not a proven cause. The game config was read at its last save and may differ from what was active during the capture.",
                ["settings.ray_tracing", "freeze.count", "fps.low1_average"],
                [
                    "Turn ray tracing off (or to its lowest level), record the same route again, and compare the two runs.",
                    "If hitches remain, it was not the main cause: continue with the other findings.",
                ]);
        }

        // 5b. Causes that need several signals: CPU limits, change over time, memory setup, Windows options.
        DeepRules.Evaluate(Add, metrics, freezes, stutters, bottleneck, trends, info, system, settings, extra, frameGen);

        // 6. Programs competing with the game.
        if (background is not null)
        {
            EvaluateBackground(Add, background, facts);
        }

        if (freezes.Count == 0 && stutters.Count <= Math.Max(5, metrics.FrameCount / 5000) &&
            metrics.AverageFps > 0 && metrics.Low1AverageFps >= metrics.AverageFps * 0.5)
        {
            Add("SMOOTH", Severity.Info, "Frame delivery looks smooth",
                "No freezes and few stutters.", ["freeze.count", "stutter.count", "fps.low1_average"], []);
        }

        return list;
    }

    internal delegate void AddFinding(string id, Severity severity, string title, string detail, string[] keys, string[] suggestions, bool localOnly = false);

    private static void EvaluateDisplay(AddFinding add, FrameMetrics metrics, ExtraSignals x, SystemContext system, bool vrr, GameSettings? settings)
    {
        var tearing = x.TearingAllowedPct >= 90 && x.SyncZeroPct >= 90;
        if (!tearing)
        {
            return;
        }

        // What the user entered about VSync wins over what a capture can see: the capture records what the game asked for,
        // and a VSync override in the GPU driver happens after that, so it is invisible in the capture.
        var vsync = system.Get("setup.vsync") ?? (settings?.IsOn("vsync") == true ? "On (in-game)" : "");
        if (vsync.Contains("driver", StringComparison.OrdinalIgnoreCase) ||
            vsync.StartsWith("Fast", StringComparison.OrdinalIgnoreCase))
        {
            add("TEARING_HANDLED_BY_DRIVER", Severity.Info, "VSync is on in the GPU driver, so tearing is unlikely",
                "The capture shows the game asking for no sync, but you said VSync is on in the driver. The driver applies it after the game, where a capture cannot see it. "
                + (vrr ? "Together with variable refresh this is the recommended setup: no tearing, and low latency as long as the frame rate stays inside the display's range." : "Without variable refresh, VSync removes tearing but can add some latency and halve the frame rate when the game cannot keep up with the refresh rate."),
                ["present.tearing_allowed_pct", "present.sync_interval_zero_pct", "setup.vsync", "display.vrr_evidence"],
                // Only worth saying when the game actually runs at or above the display's maximum.
                vrr && ((x.FramesAboveRefreshPct ?? 0) >= 5 || (x.RefreshMaxHz is { } max && metrics.AverageFps >= max - 3))
                    ? ["Keep the frame rate a few fps below the monitor's maximum (a cap such as 141 on a 144 Hz monitor) so VSync never has to wait."]
                    : []);
            return;
        }

        if (vsync.StartsWith("On", StringComparison.OrdinalIgnoreCase))
        {
            add("VSYNC_ENTERED_BUT_NOT_SEEN", Severity.Info, "You said VSync is on, but this capture shows no sync",
                "The capture shows frames presented without VSync. Either the setting was changed after this capture was recorded, or the game ignored it. Record a new run to check. I cannot see tearing itself, only the conditions for it.",
                ["present.tearing_allowed_pct", "present.sync_interval_zero_pct", "setup.vsync"],
                ["Record the same route again with the current settings."]);
            return;
        }

        var above = x.FramesAboveRefreshPct ?? 0;
        var capEmpty = !system.Items.Any(i => i.Key == "setup.frame_cap");
        string[] keys = ["present.tearing_allowed_pct", "present.sync_interval_zero_pct", "display.vrr_evidence", "display.refresh_max_hz", "display.frames_above_refresh_pct"];

        if (vrr && above < 5)
        {
            add("TEARING_UNLIKELY", Severity.Info, "Sync is off, but variable refresh is active",
                "Frames were presented without VSync, yet the display's refresh rate follows the frame rate and the game stays inside its range, so tearing is unlikely. I cannot see tearing itself, only the conditions for it.",
                keys, []);
        }
        else if (vrr)
        {
            add("TEARING_ABOVE_RANGE", Severity.Medium, "Frame rate goes above the display's refresh range",
                $"{F(above)}% of frames ran faster than the display's maximum ({F(x.RefreshMaxHz ?? 0)} Hz) with sync off. Above the range, variable refresh stops helping and tearing can appear."
                + (capEmpty ? " No frame cap was entered." : ""),
                keys,
                [
                    "Set a frame cap about 3 fps below your monitor's maximum refresh rate (for example 141 on a 144 Hz monitor).",
                    "With G-SYNC, also turn on VSync in the GPU driver (not in the game) so the cap and VRR work together.",
                ]);
        }
        else
        {
            add("TEARING_POSSIBLE", Severity.Medium, "Sync is off and no variable refresh was seen",
                "Frames were presented without VSync and the display refresh rate stayed fixed, so tearing is possible whenever the frame rate and refresh rate do not line up. I cannot see tearing itself, only the conditions for it.",
                keys,
                [
                    "If your monitor supports G-SYNC or FreeSync, turn it on in the driver and in the monitor menu.",
                    "Otherwise turn on VSync (in the game or the driver). It removes tearing but can add some latency.",
                    "A frame cap just below the refresh rate keeps frame times steady.",
                ]);
        }
    }

    private static void EvaluateSystem(AddFinding add, SystemContext system, ExtraSignals x, int freezes, int stutters, FrameMetrics metrics)
    {
        if (system.Is("game_drive.media", "HDD"))
        {
            add("GAME_ON_HDD", freezes > 0 || stutters > 20 ? Severity.Medium : Severity.Low,
                "The game is installed on a hard disk (HDD)",
                "Hard disks are much slower than SSDs at loading and streaming game assets, which causes hitches when new areas load.",
                ["system.game_drive.media"],
                [
                    "Move the game to an SSD (an NVMe drive if you can). Most launchers have a 'move install' option.",
                    "Until then, keep other disk activity (downloads, updates, scans) off while playing.",
                ]);
        }

        if (double.TryParse(system.Get("game_drive.free_pct"), NumberStyles.Float, CultureInfo.InvariantCulture, out var free) && free < 10)
        {
            add("DRIVE_NEARLY_FULL", Severity.Low, $"The game drive is nearly full ({F(free)}% free)",
                "SSDs slow down when they are almost full, and Windows needs free space for temporary files.",
                ["system.game_drive.free_pct"],
                ["Free up space so at least 15 to 20% of the drive is empty."]);
        }

        if (double.TryParse(system.Get("shader_cache_mb"), NumberStyles.Float, CultureInfo.InvariantCulture, out var cache) && cache < 100 && freezes > 0)
        {
            add("SHADER_CACHE_SMALL", Severity.Low, "The driver shader cache is nearly empty",
                $"It holds only {F(cache)} MB. After a driver update or a cache clean-up, games compile shaders while you play, which can cause freezes the first time through an area.",
                ["system.shader_cache_mb", "freeze.count"],
                [
                    "Play through the same area twice. If the second pass is smooth, shader compilation was the cause.",
                    "Do not delete the shader cache. In the GPU driver, leave the shader cache size on Default or Unlimited.",
                ]);
        }

        if (system.Is("power_plan", "Power saver"))
        {
            add("POWER_PLAN_SAVER", Severity.Medium, "Windows is on the Power saver plan",
                "This plan limits CPU speed to save energy, which lowers game performance.",
                ["system.power_plan"],
                ["Switch the power plan to Balanced or High performance in Windows power settings."]);
        }

        if (double.TryParse(system.Get("display_current_hz"), NumberStyles.Float, CultureInfo.InvariantCulture, out var nowHz) &&
            double.TryParse(system.Get("setup.monitor_max_hz"), NumberStyles.Float, CultureInfo.InvariantCulture, out var maxHz) && nowHz > 0 && maxHz > nowHz + 1)
        {
            add("DISPLAY_BELOW_MAX", Severity.Medium, $"Windows runs your display at {F(nowHz)} Hz, below its maximum of {F(maxHz)} Hz",
                "A lower refresh rate makes the game look less smooth and can cause tearing or latency.",
                ["system.display_current_hz", "setup.monitor_max_hz"],
                ["Open Windows Settings > System > Display > Advanced display and choose the highest refresh rate."]);
        }
    }

    private static void EvaluateBackground(AddFinding add, BackgroundScanResult scan, IReadOnlyDictionary<string, string> facts)
    {
        var keys = new[] { "background.other_cpu_pct", "background.other_disk_mbps", "background.other_gpu_pct", "background.known_categories" };
        var cpuTop = scan.Others.Where(p => p.CpuPct >= 1.5).OrderByDescending(p => p.CpuPct).Take(3).ToList();
        var diskTop = scan.Others.Where(p => p.DiskMBps >= 1).OrderByDescending(p => p.DiskMBps).Take(3).ToList();
        var gpuTop = scan.Others.Where(p => (p.GpuPct ?? 0) >= 1.5).OrderByDescending(p => p.GpuPct).Take(3).ToList();
        var any = false;

        if (scan.OtherCpuPct >= 8)
        {
            any = true;
            add("BACKGROUND_CPU", scan.OtherCpuPct >= 15 ? Severity.Medium : Severity.Low,
                $"Other programs use {F(scan.OtherCpuPct)}% of the CPU",
                $"Biggest: {string.Join(", ", cpuTop.Select(p => $"{p.Name} ({F(p.CpuPct)}%)"))}. {BackgroundCaveat}",
                keys, ["Close or pause these programs while gaming, then run the check again.", .. AdviceFor(cpuTop)], localOnly: true);
        }

        if (scan.OtherDiskMBps >= 5)
        {
            any = true;
            add("BACKGROUND_DISK", scan.OtherDiskMBps >= 20 ? Severity.Medium : Severity.Low,
                $"Other programs are reading and writing {F(scan.OtherDiskMBps)} MB/s",
                $"Biggest: {string.Join(", ", diskTop.Select(p => $"{p.Name} ({F(p.DiskMBps)} MB/s)"))}. Heavy disk use slows game loading and streaming. {BackgroundCaveat}",
                keys, ["Pause downloads, updates, backups, cloud sync and scans while you play.", .. AdviceFor(diskTop)], localOnly: true);
        }

        if (scan.OtherGpuPct is { } gpuPct && gpuPct >= 3)
        {
            any = true;
            add("BACKGROUND_GPU", gpuPct >= 8 ? Severity.Medium : Severity.Low,
                $"Other programs use {F(gpuPct)}% of the GPU",
                $"Biggest: {string.Join(", ", gpuTop.Select(p => $"{p.Name} ({F(p.GpuPct ?? 0)}%)"))}. {BackgroundCaveat}",
                keys, ["Close browsers, video players, recorders and overlays while gaming.", .. AdviceFor(gpuTop)], localOnly: true);
        }

        var known = scan.KnownInterferers.ToList();
        if (known.Count > 0)
        {
            var categories = known.Select(p => p.Category).Distinct().Order().ToList();
            add("BACKGROUND_KNOWN_APPS", any ? Severity.Low : Severity.Info,
                $"Programs that often affect games are running ({string.Join(", ", categories)})",
                $"Running: {string.Join(", ", known.Select(p => p.Name).Distinct().Take(8))}. Being open does not mean they hurt performance; they are the usual suspects to rule out. {BackgroundCaveat}",
                keys, known.Select(p => p.Advice).Where(a => a.Length > 0).Distinct().Take(4).ToArray(), localOnly: true);
        }
        else if (!any)
        {
            add("BACKGROUND_QUIET", Severity.Info, "No background program stood out",
                $"No other program used noticeable CPU, disk or GPU during the {scan.DurationSec:0}-second check. {BackgroundCaveat}",
                keys, []);
        }
    }

    private static string[] AdviceFor(IEnumerable<ProcessUsage> processes) =>
        processes.Select(p => p.Advice).Where(a => a.Length > 0).Distinct().Take(3).ToArray();

    private static string[] CoolingSuggestions() =>
    [
        "Clean dust from case filters, heatsinks and fans, and check every fan spins.",
        "Improve airflow: free vents, sensible fan curves, keep the PC away from heat sources.",
        "Cap the frame rate or lower settings to reduce heat while you investigate.",
    ];

    private static bool UserSaysVrr(SystemContext system)
    {
        var sync = system.Get("setup.display_sync");
        return sync is not null &&
               (sync.Contains("G-SYNC", StringComparison.OrdinalIgnoreCase) ||
                sync.Contains("FreeSync", StringComparison.OrdinalIgnoreCase) ||
                sync.StartsWith("VRR", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The most important actions, most severe first, without repeating the same advice.</summary>
    public static IReadOnlyList<NextStep> BuildNextSteps(IReadOnlyList<Finding> findings, int max = 6)
    {
        var steps = new List<NextStep>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 1;

        foreach (var f in findings.Where(f => f.Suggestions.Count > 0).OrderByDescending(f => f.Severity))
        {
            foreach (var text in f.Suggestions.Take(f.Severity >= Severity.Medium ? 2 : 1))
            {
                if (steps.Count < max && seen.Add(text))
                {
                    steps.Add(new NextStep(order++, text, f.Id, f.Title, f.Severity));
                }
            }
        }

        return steps;
    }

    /// <summary>"Drive activity (nvme/1)" becomes "NVMe drive 1 (nvme/1)" so nobody has to know the monitoring software's naming.</summary>
    private static string FriendlyDrive(string label)
    {
        var open = label.IndexOf('(', StringComparison.Ordinal);
        var close = label.IndexOf(')', StringComparison.Ordinal);
        if (open < 0 || close < open)
        {
            return label;
        }

        var id = label[(open + 1)..close];
        var parts = id.Split('/');
        var kind = parts[0].Equals("nvme", StringComparison.OrdinalIgnoreCase) ? "NVMe" : "SATA or other";
        return parts.Length > 1 ? $"{kind} drive {parts[1]} ({id})" : label;
    }

    /// <summary>Says what is known about which drive the game is on, since the monitoring software cannot tell us.</summary>
    private static string GameDriveNote(SystemContext system, string busyLabel)
    {
        var media = system.Get("game_drive.media");
        if (media is null)
        {
            return "Which drive holds the game is not known: set the install folder in PC setup to find out.";
        }

        var bus = system.Get("game_drive.bus");
        var busyIsNvme = busyLabel.Contains("nvme", StringComparison.OrdinalIgnoreCase);
        var gameIsNvme = bus?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) == true;
        var what = bus is null ? media : $"{media} ({bus})";
        var same = busyIsNvme == gameIsNvme
            ? "That is the same kind of drive, so it may be the game's drive, but the numbers are the monitoring software's order and do not say for sure."
            : "That is a different kind of drive from the game's, so the spike was probably another drive.";
        return $"The game is installed on a {what}. {same}";
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
