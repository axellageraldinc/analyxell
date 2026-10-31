using System.Globalization;
using System.Text.RegularExpressions;
using Analyxell.Core.Capture;
using Analyxell.Core.Config;
using Analyxell.Core.SystemInfo;

namespace Analyxell.Core.Analysis;

/// <summary>Hitches that come back at a steady interval, which points at something on a timer rather than at the game.</summary>
internal sealed record HitchPattern(double PeriodSec, int Events)
{
    private const int MinEvents = 6;

    /// <summary>
    /// Looks at when freezes and stutters happened. Events within a second of each other count as one. If most of the
    /// gaps between them are about the same length, the hitches are periodic.
    /// </summary>
    public static HitchPattern? Find(IReadOnlyList<Freeze> freezes, IReadOnlyList<Stutter> stutters)
    {
        var times = freezes.Select(f => f.TimeSec).Concat(stutters.Select(s => s.TimeSec)).Order().ToList();
        var events = new List<double>();
        foreach (var t in times)
        {
            if (events.Count == 0 || t - events[^1] >= 1.0)
            {
                events.Add(t);
            }
        }

        if (events.Count < MinEvents)
        {
            return null;
        }

        var gaps = events.Zip(events.Skip(1), (a, b) => b - a).ToList();
        var median = gaps.Order().ElementAt(gaps.Count / 2);
        if (median is < 3 or > 900)
        {
            return null;
        }

        var regular = gaps.Count(g => Math.Abs(g - median) <= median * 0.15);
        return regular >= gaps.Count * 0.7 && regular >= MinEvents - 1 ? new HitchPattern(median, events.Count) : null;
    }
}

/// <summary>Average load on the first and second half of a Ryzen's cores, which on a two-chiplet chip are the two chiplets.</summary>
internal sealed partial record CcdLoad(double FirstPct, double SecondPct, int Cores)
{
    public static CcdLoad? Compute(CaptureRun run, CpuProfile cpu)
    {
        if (!cpu.DualCcd)
        {
            return null;
        }

        var perCore = new SortedDictionary<int, List<double>>();
        foreach (var sensor in run.Sensors.Where(s => s.Type == "Load"))
        {
            var m = CoreLoadName().Match(sensor.Name);
            if (!m.Success)
            {
                continue;
            }

            var summary = Stats.Summarize(sensor.Values);
            if (summary.Count == 0)
            {
                continue;
            }

            var core = int.Parse(m.Groups["core"].Value, CultureInfo.InvariantCulture);
            if (!perCore.TryGetValue(core, out var list))
            {
                perCore[core] = list = [];
            }

            list.Add(summary.Average);
        }

        // Two chiplets of at least six cores each; anything smaller is not a layout we can read from the sensor names.
        if (perCore.Count < 12 || perCore.Count % 2 != 0)
        {
            return null;
        }

        var averages = perCore.Values.Select(v => v.Average()).ToArray();
        var half = averages.Length / 2;
        return new CcdLoad(averages.Take(half).Average(), averages.Skip(half).Average(), averages.Length);
    }

    [GeneratedRegex(@"^Core #(?<core>\d+)(?:\s+Thread #\d+)?$")]
    private static partial Regex CoreLoadName();
}

/// <summary>
/// Findings that depend on the exact hardware, for Ryzen processors and NVIDIA graphics cards: how Windows places the
/// game on two-chiplet chips, whether the CPU reaches its rated boost, whether the GPU or CPU was tuned, and what
/// Windows logged about unstable hardware. They read the model names, so they work for any Ryzen and any GeForce.
/// </summary>
internal static class HardwareRules
{
    public static void Evaluate(
        Rules.AddFinding add,
        CaptureRun run,
        FrameMetrics metrics,
        IReadOnlyList<Freeze> freezes,
        IReadOnlyList<Stutter> stutters,
        BottleneckResult bottleneck,
        RunTrends trends,
        CaptureInfo info,
        SystemContext system,
        GameSettings? settings,
        ExtraSignals extra,
        FrameGenerationSignal frameGen,
        IReadOnlyDictionary<string, string> facts)
    {
        var cpu = CpuProfile.Parse(info.Cpu);
        var gpu = GpuProfile.Parse(info.Gpu);
        var hitches = freezes.Count > 0 || stutters.Count > 20 || metrics.Low1AverageFps < metrics.AverageFps * 0.6;
        var cpuBound = bottleneck.Verdict == BottleneckVerdict.CpuBound;

        PeriodicHitches(add, cpu, HitchPattern.Find(freezes, stutters));

        if (cpu.IsRyzen)
        {
            Chiplets(add, cpu, CcdLoad.Compute(run, cpu), system, hitches, cpuBound);
            Boost(add, cpu, run, bottleneck, system, hitches, cpuBound);
            AmdDrivers(add, system, hitches, cpuBound);
            MemoryController(add, cpu, facts);
        }

        if (gpu.IsNvidia)
        {
            ClocksAtLightLoad(add, run, bottleneck, hitches);
            Tuning(add, system, bottleneck, run, hitches);
            FrameGenerationNeeds(add, gpu, info, system, frameGen);
            UpscalerAvailable(add, gpu, system, settings, bottleneck, metrics);
        }

        Stability(add, system, cpu, hitches);
    }

    private static void PeriodicHitches(Rules.AddFinding add, CpuProfile cpu, HitchPattern? pattern)
    {
        if (pattern is null)
        {
            return;
        }

        var every = pattern.PeriodSec >= 60 ? $"{pattern.PeriodSec / 60:0.#} minutes" : $"{pattern.PeriodSec:0} seconds";
        var suggestions = new List<string>
        {
            "Something on a timer is the usual cause. Close monitoring and RGB software, overlays, cloud sync and launchers one at a time, and record again to see which one makes it stop.",
            "For online games, a Wi-Fi adapter that scans for networks can cause a hitch at a steady interval: try a cable, or turn off background scanning for the adapter.",
        };
        if (cpu.IsRyzen && pattern.PeriodSec >= 20)
        {
            suggestions.Add("On AMD Ryzen, firmware TPM (fTPM) can pause the PC briefly at intervals on some boards. Update the BIOS first; if it persists, check your motherboard maker's notes on fTPM.");
        }

        add("PERIODIC_HITCHES", Severity.Medium,
            $"Hitches repeat about every {every}",
            $"{pattern.Events} hitches were spaced almost evenly, about {every} apart. Random trouble does not look like this: a steady rhythm points at something running on a timer in the background, not at the game's own loading.",
            ["hitch.period_sec", "hitch.periodic_events", "stutter.count"],
            [.. suggestions]);
    }

    /// <summary>A two-chiplet Ryzen only helps a game if the game stays on the right chiplet.</summary>
    private static void Chiplets(Rules.AddFinding add, CpuProfile cpu, CcdLoad? load, SystemContext system, bool hitches, bool cpuBound)
    {
        if (load is null || !(hitches || cpuBound))
        {
            return;
        }

        var driverMissing = system.Is("amd_vcache_driver", "Not found");
        if (cpu.X3D)
        {
            if (load.SecondPct > load.FirstPct * 1.3 && load.SecondPct >= 15)
            {
                add("VCACHE_WRONG_CHIPLET", Severity.Medium,
                    "The game seems to be running on the chiplet without the extra cache",
                    $"On this two-chiplet X3D processor the first {load.Cores / 2} cores have the large cache and the other {load.Cores / 2} do not. The game kept those other cores busier ({load.SecondPct:0}% vs {load.FirstPct:0}% on average), which gives up what an X3D chip is for. This is a likely explanation from core load, not proven.",
                    ["cpu.ccd0_load_avg", "cpu.ccd1_load_avg", "system.amd_vcache_driver"],
                    [
                        driverMissing
                            ? "Install the AMD chipset driver including the 3D V-Cache driver: it was not found on this PC."
                            : "Reinstall the latest AMD chipset driver, which carries the 3D V-Cache driver that places games on the cache chiplet.",
                        "Turn Windows Game Mode on and keep Xbox Game Bar enabled: the V-Cache driver relies on Windows recognising the game.",
                        "To test it, open Task Manager > Details, right-click the game and choose Set affinity to the first chiplet's cores only, then record again and compare.",
                    ]);
            }

            return;
        }

        if (load.FirstPct >= 20 && load.SecondPct >= 20 && Math.Max(load.FirstPct, load.SecondPct) <= Math.Min(load.FirstPct, load.SecondPct) * 1.7)
        {
            add("CHIPLET_SPLIT", Severity.Low,
                "The game's work is spread across both CPU chiplets",
                $"Both halves of this processor were busy ({load.FirstPct:0}% and {load.SecondPct:0}%). Passing data between the two chiplets takes longer than inside one, which can cause uneven frame times in games that do not scale well. This is a possible cause to test, not a proven one.",
                ["cpu.ccd0_load_avg", "cpu.ccd1_load_avg"],
                [
                    "To test it, open Task Manager > Details, right-click the game, choose Set affinity and keep only the first chiplet's cores, then record again and compare.",
                    "Keep the AMD chipset driver current: it helps Windows place threads sensibly.",
                ]);
        }
    }

    /// <summary>Compares the fastest core's clock with what the processor is rated for.</summary>
    private static void Boost(Rules.AddFinding add, CpuProfile cpu, CaptureRun run, BottleneckResult bottleneck, SystemContext system, bool hitches, bool cpuBound)
    {
        if (cpu.RatedBoostMhz is not { } rated || run.FindSensor("CPU Max", "Clock") is not { } clock)
        {
            return;
        }

        var summary = Stats.Summarize(clock.Values);
        if (summary.Count == 0 || !double.IsFinite(summary.Max))
        {
            return;
        }

        var peak = summary.Max;
        if (peak >= rated + 150)
        {
            add("CPU_ABOVE_RATED", Severity.Info,
                "The CPU boosted above its rated speed",
                $"The fastest core reached {peak:0} MHz, {peak - rated:0} MHz above the {rated:0} MHz this model is rated for. That is what Precision Boost Overdrive or a manual overclock does, so the CPU has been tuned. This is not a problem by itself; it matters if the game crashes or freezes.",
                ["cpu.clock_peak_mhz", "cpu.rated_boost_mhz"],
                hitches
                    ? ["If you see freezes, crashes or black screens, retest with PBO and Curve Optimizer off. If the problem goes away, the tuning is not fully stable."]
                    : []);
        }
        else if (cpuBound && peak < rated * 0.93)
        {
            var hot = bottleneck.CpuTempMax is >= 90;
            var plan = system.Get("power_plan");
            var suggestions = new List<string>();
            if (plan is not null && !plan.Contains("performance", StringComparison.OrdinalIgnoreCase) && !plan.Equals("Balanced", StringComparison.OrdinalIgnoreCase))
            {
                suggestions.Add("Set the Windows power plan to Balanced or High performance.");
            }

            suggestions.Add(system.Is("amd_chipset_driver", "Not found")
                ? "Install the AMD chipset driver: it was not found on this PC, and it sets up how Windows boosts Ryzen cores."
                : "Update the AMD chipset driver and the BIOS.");
            suggestions.Add("In the BIOS, check that Eco Mode and any power limits (PPT, TDC, EDC) are not set lower than you intend.");
            if (hot)
            {
                suggestions.Add("The CPU also ran hot: improve cooling, since a Ryzen lowers its boost as it heats up.");
            }

            add("CPU_BELOW_BOOST", Severity.Medium,
                "The CPU never reached its rated boost speed",
                $"The game was limited by the CPU, yet the fastest core peaked at {peak:0} MHz, under the {rated:0} MHz this model is rated for. A CPU-limited game is exactly when full boost matters. This is a sensor reading and can be off by a few percent.",
                ["cpu.clock_peak_mhz", "cpu.rated_boost_mhz", "cpu.temp_max_c", "bottleneck.verdict"],
                [.. suggestions]);
        }
    }

    private static void AmdDrivers(Rules.AddFinding add, SystemContext system, bool hitches, bool cpuBound)
    {
        if (system.Is("amd_chipset_driver", "Not found"))
        {
            add("AMD_CHIPSET_MISSING", hitches || cpuBound ? Severity.Medium : Severity.Low,
                "The AMD chipset driver was not found",
                "The chipset driver adds the Ryzen power plan and tells Windows how to place and boost the cores. Without it, Windows uses generic handling, which can cost frame rate and smoothness. It only checks installed programs, so a driver installed some other way may not show.",
                ["system.amd_chipset_driver"],
                ["Download the AMD chipset driver for your motherboard's chipset from amd.com (or your motherboard maker) and install it."]);
        }
    }

    /// <summary>Ryzen 7000 and 9000 run their memory controller at half speed above about DDR5-6400, which usually costs games more than the extra speed gives.</summary>
    private static void MemoryController(Rules.AddFinding add, CpuProfile cpu, IReadOnlyDictionary<string, string> facts)
    {
        if (cpu.Series is not (7000 or 9000) || cpu.Mobile ||
            !facts.TryGetValue("pc.memory_mts", out var text) ||
            !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed) || speed < 7200)
        {
            return;
        }

        add("RAM_ABOVE_SWEET_SPOT", Severity.Low,
            $"Memory is set to DDR5-{speed:0}, above where Ryzen runs at full efficiency",
            "Ryzen 7000 and 9000 keep the memory controller in step with the memory only up to about DDR5-6000 to 6400. Above that it runs at half speed, which usually lowers game performance despite the higher number. This depends on the board and the BIOS.",
            ["pc.memory_mts"],
            ["Try the DDR5-6000 EXPO profile (or set the speed to 6000 with tight timings) and compare frame rates."]);
    }

    /// <summary>The GPU clock falling well below its normal speed while the game is still running, which feels like stutter.</summary>
    private static void ClocksAtLightLoad(Rules.AddFinding add, CaptureRun run, BottleneckResult b, bool hitches)
    {
        if (!hitches || b.GpuLoadAvg is not (>= 35 and <= 85) ||
            run.FindSensor("GPU Core", "Clock") is not { } clock)
        {
            return;
        }

        var values = clock.Values.Where(v => double.IsFinite(v) && v > 0).Order().ToArray();
        if (values.Length < 20)
        {
            return;
        }

        var low = Stats.Percentile(values, 0.10);
        var high = Stats.Percentile(values, 0.95);
        if (low >= high * 0.6)
        {
            return;
        }

        add("GPU_CLOCK_DROPS", Severity.Low,
            "The GPU clock dropped far below its normal speed during play",
            $"The GPU clock fell to {low:0} MHz in the slowest tenth of the run while it reached {high:0} MHz otherwise, with the GPU only {b.GpuLoadAvg:0}% loaded on average. When the game needs a burst of work, a card that has slowed down needs a moment to speed up, which can feel like a hitch. This is a likely explanation, not proven.",
            ["gpu.load_avg", "gpu.clock_p10_mhz", "gpu.clock_p95_mhz"],
            [
                "In NVIDIA Control Panel > Manage 3D settings > Program Settings, pick this game and set Power management mode to Prefer maximum performance.",
                "Make sure the Windows power plan is not Power saver.",
                "Record again and compare: if hitches stay, this was not the cause.",
            ]);
    }

    /// <summary>The GPU's clock offsets and power limit as set now, and what they say about tuning.</summary>
    private static void Tuning(Rules.AddFinding add, SystemContext system, BottleneckResult b, CaptureRun run, bool hitches)
    {
        var core = Number(system, "gpu_tuning.core_offset_mhz");
        var memory = Number(system, "gpu_tuning.memory_offset_mhz");
        var limit = Number(system, "gpu_tuning.power_limit_w");
        var stock = Number(system, "gpu_tuning.power_default_w");
        const string Caveat = " These are the settings now, which may differ from when the capture was recorded.";

        // Offsets are not readable on every card, so a clock above the card's own stock maximum also counts.
        var peakClock = run.FindSensor("GPU Core", "Clock") is { } coreClock && Stats.Summarize(coreClock.Values) is { Count: > 0 } clockStats ? clockStats.Max : (double?)null;
        var overMax = Number(system, "gpu_tuning.max_clock_mhz") is { } stockMax && peakClock is { } seen && seen >= stockMax + 45 ? seen - stockMax : (double?)null;

        if (core >= 50 || memory >= 200 || overMax is not null)
        {
            var parts = new List<string>();
            if (core >= 50 || overMax is not null)
            {
                parts.Add(core >= 50 ? $"core clock +{core:0} MHz" : $"core clock {overMax:0} MHz past the card's stock maximum");
            }

            if (memory >= 200)
            {
                parts.Add($"memory clock +{memory:0} MHz");
            }

            add("GPU_OVERCLOCKED", hitches ? Severity.Low : Severity.Info,
                "The GPU is overclocked",
                $"The driver reports {string.Join(" and ", parts)} over stock.{Caveat} An overclock is fine until it is not quite stable, which shows up as freezes, driver resets or crashes.",
                ["system.gpu_tuning.core_offset_mhz", "system.gpu_tuning.memory_offset_mhz", "system.gpu_tuning.max_clock_mhz", "gpu.clock_p95_mhz"],
                hitches
                    ? [
                        "Set the offsets back to 0 for one test, record again and compare. If the freezes go away, the overclock is at its limit.",
                        "On GDDR6X cards a memory overclock that is too high can lower performance without crashing, because the memory corrects its own errors. Compare fps with the memory offset at 0.",
                    ]
                    : []);
        }
        else if (core <= -50)
        {
            add("GPU_CLOCKS_LOWERED", Severity.Low, "The GPU's core clock is set below stock",
                $"The driver reports the core clock {core:0} MHz under stock.{Caveat} That costs frame rate when the GPU is the limit.",
                ["system.gpu_tuning.core_offset_mhz"],
                ["If this was not intended, set the offset back to 0 in your GPU tuning software."]);
        }

        if (limit is { } now && stock is { } def && def > 0 && now < def * 0.97)
        {
            add("GPU_POWER_LIMIT_LOWERED", b.Verdict is BottleneckVerdict.PowerLimited or BottleneckVerdict.GpuBound ? Severity.Medium : Severity.Low,
                $"The GPU's power limit is set below default ({now:0} W vs {def:0} W)",
                $"Something lowered the power limit to {now / def * 100:0}% of default.{Caveat} The card slows down when it reaches that limit, so a lowered limit costs frame rate when the GPU is busy.",
                ["system.gpu_tuning.power_limit_w", "system.gpu_tuning.power_default_w", "gpu.power_limit_hits_pct"],
                ["If you did not set this on purpose, raise the power limit back to 100% in your GPU tuning software."]);
        }

        // A card held at full clocks while drawing much less than its limit has usually been undervolted. The voltage curve itself cannot be read.
        if (limit is { } cap && b.GpuLoadAvg >= 95 && run.FindSensor("GPU Power", "Power") is { } power && run.FindSensor("GPU Core", "Clock") is { } clock)
        {
            var watts = Stats.Summarize(power.Values);
            var clocks = clock.Values.Where(v => double.IsFinite(v) && v > 0).Order().ToArray();
            if (watts.Count > 0 && clocks.Length >= 20 && watts.Average < cap * 0.8 && (b.PowerLimitHitsPct ?? 0) < 5
                && Stats.Percentile(clocks, 0.10) >= Stats.Percentile(clocks, 0.95) * 0.95)
            {
                add("UNDERVOLT_LIKELY", Severity.Info,
                    "The GPU looks undervolted",
                    $"It held {Stats.Percentile(clocks, 0.5):0} MHz at full load while drawing only {watts.Average:0} W of its {cap:0} W limit. That pairing is what an undervolt gives. It could also be a light workload, so this is an inference: voltage curves cannot be read.",
                    ["gpu.load_avg", "gpu.power_limit_hits_pct", "system.gpu_tuning.power_limit_w"],
                    hitches ? ["If you see freezes or crashes, return the voltage curve to stock for one test and compare."] : []);
            }
        }
    }

    private static void FrameGenerationNeeds(Rules.AddFinding add, GpuProfile gpu, CaptureInfo info, SystemContext system, FrameGenerationSignal frameGen)
    {
        var hagsOff = system.Is("hags", "Off") || info.Hags.Equals("Disabled", StringComparison.OrdinalIgnoreCase);
        if (frameGen.Active && gpu.SupportsDlssFrameGeneration && hagsOff)
        {
            add("HAGS_OFF_WITH_FRAMEGEN", Severity.Low,
                "Hardware-accelerated GPU scheduling is off while frame generation is on",
                "NVIDIA recommends Windows' hardware-accelerated GPU scheduling for DLSS frame generation. With it off, frame generation can pace frames less evenly.",
                ["pc.hags", "framegen.detected"],
                ["Turn it on in Windows Settings > System > Display > Graphics > Default graphics settings, then restart and record again."]);
        }
    }

    /// <summary>A game that ships DLSS, running below 60 fps on a GPU that is the limit, on a card that can use it.</summary>
    private static void UpscalerAvailable(Rules.AddFinding add, GpuProfile gpu, SystemContext system, GameSettings? settings, BottleneckResult b, FrameMetrics m)
    {
        var shipped = system.Get("game_upscalers");
        var gpuLimited = b.Verdict is BottleneckVerdict.GpuBound or BottleneckVerdict.PowerLimited;
        if (shipped is null || !gpu.SupportsDlss || !gpuLimited || m.AverageFps >= 60 || settings?.Get("dlss") is not null || !shipped.Contains("DLSS", StringComparison.Ordinal))
        {
            return;
        }

        var suggestions = new List<string>
        {
            "Look for DLSS (or Super Resolution) in the game's graphics settings. Quality mode keeps most of the sharpness and raises the frame rate the most for the least loss.",
        };
        if (shipped.Contains("Frame Generation", StringComparison.Ordinal) && gpu.SupportsDlssFrameGeneration && m.AverageFps >= 40)
        {
            suggestions.Add(gpu.SupportsMultiFrameGeneration
                ? "Frame generation (including multi frame generation on your card) can raise the displayed frame rate further. It needs about 40 to 60 fps rendered to feel good, and NVIDIA Reflex on."
                : "Frame generation can raise the displayed frame rate further. It needs about 40 to 60 fps rendered to feel good, and NVIDIA Reflex on.");
        }

        suggestions.Add("Record again and use Compare to check the gain.");
        add("UPSCALER_AVAILABLE", Severity.Low,
            "This game ships DLSS and the GPU is the limit",
            $"The game's folder includes {shipped}, the average is {m.AverageFps:0} fps and the GPU is the limit. A capture cannot show whether upscaling is already on, so check the game's settings.",
            ["system.game_upscalers", "fps.average", "bottleneck.verdict"],
            [.. suggestions]);
    }

    /// <summary>Hardware errors and driver resets that Windows logged: the honest sign of an unstable overclock, undervolt or memory profile.</summary>
    private static void Stability(Rules.AddFinding add, SystemContext system, CpuProfile cpu, bool hitches)
    {
        var whea = Number(system, "events.whea_errors");
        var resets = Number(system, "events.display_resets");
        const string Window = " in the last 30 days";

        if (whea >= 1)
        {
            var tuning = cpu.IsRyzen ? "a Curve Optimizer or PBO undervolt that is too aggressive, an unstable memory (EXPO) profile, " : "an overclock or undervolt that is too aggressive, an unstable memory profile, ";
            add("HARDWARE_ERRORS_LOGGED", whea >= 3 ? Severity.Medium : Severity.Low,
                $"Windows logged {whea:0} hardware error(s){Window}",
                $"These are errors the processor or memory reported to Windows (WHEA). The usual causes are {tuning}or failing hardware. A game that freezes or crashes with these logged is often a stability problem, not a game problem. They are not tied to this capture; they cover the whole period.",
                ["system.events.whea_errors"],
                [
                    "If you use any tuning (Curve Optimizer, PBO, an overclock, EXPO), set it back to default for a day of play. If the errors stop, ease the tuning back in step by step.",
                    "Make sure the BIOS and chipset driver are current.",
                    "If the errors continue at stock settings, have the hardware checked.",
                ]);
        }

        if (resets >= 1)
        {
            add("DISPLAY_DRIVER_RESETS", whea >= 1 || hitches || resets >= 3 ? Severity.Medium : Severity.Low,
                $"The graphics driver reset {resets:0} time(s){Window}",
                "Windows restarted the graphics driver after it stopped responding (event 4101). Typical causes are an unstable GPU overclock or undervolt, an overheated card, a weak or loose power connection, or a driver bug. Not tied to this capture; it covers the whole period.",
                ["system.events.display_resets"],
                [
                    "Set the GPU back to stock (no core or memory offset, default power limit, no custom voltage curve) and play again.",
                    "Check the card's power cables are fully seated and that its temperatures stay in range.",
                    "Install a clean copy of the latest driver, or the previous driver if the resets began after an update.",
                ]);
        }
    }

    private static double? Number(SystemContext system, string key) =>
        double.TryParse(system.Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
}
