using System.Globalization;
using System.Text.RegularExpressions;
using Daignostic.Core.Capture;
using Daignostic.Core.SystemInfo;

namespace Daignostic.Core.Analysis;

public sealed record SpecItem(string Label, string Value);

public sealed record SpecSection(string Title, IReadOnlyList<SpecItem> Items);

/// <summary>
/// The PC behind a capture: a one-line summary for the page and detailed sections behind a button. This is for the
/// user's screen only. The motherboard is shown here but is never put into AI messages or reports.
/// </summary>
public sealed partial record PcSpecs(string Summary, IReadOnlyList<SpecSection> Sections)
{
    public static PcSpecs Build(CaptureInfo info, CaptureRun run, ExtraSignals extra, BottleneckResult bottleneck, SystemContext system)
    {
        var summary = string.Join("  ·  ", new[] { info.Gpu, info.Cpu, info.SystemRam, info.Resolution }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var sections = new List<SpecSection>();

        void Section(string title, params (string Label, string? Value)[] items)
        {
            var kept = items.Where(i => !string.IsNullOrWhiteSpace(i.Value)).Select(i => new SpecItem(i.Label, i.Value!)).ToList();
            if (kept.Count > 0)
            {
                sections.Add(new SpecSection(title, kept));
            }
        }

        string? Range(string name, string type, string unit)
        {
            var s = SensorStats(run, name, type);
            return s is null ? null : $"{F(s.Value.Min)} / {F(s.Value.Average)} / {F(s.Value.Max)} {unit}  (min / avg / max)";
        }

        Section("Graphics",
            ("GPU", info.Gpu),
            ("Driver", info.GpuDriver),
            ("Graphics API", info.GraphicsApi),
            ("Video memory", bottleneck.VramTotalGb is { } v ? $"{F(v)} GB" : null),
            ("Video memory used", bottleneck.VramUsedMaxGb is { } u ? $"up to {F(u)} GB" : null),
            ("Core clock", Range("GPU Core", "Clock", "MHz")),
            ("Memory clock", Range("GPU Memory", "Clock", "MHz")),
            ("Power draw", Range("GPU Power", "Power", "W")),
            ("Temperature", Range("GPU Core", "Temperature", "°C")),
            ("Hot spot", Range("GPU Hot Spot", "Temperature", "°C")),
            ("Resizable BAR", info.ResizableBar));

        var cores = run.Sensors.Count(s => s.Type == "Clock" && CoreName().IsMatch(s.Name));
        var threads = run.Sensors.Count(s => s.Type == "Load" && ThreadName().IsMatch(s.Name));
        Section("Processor",
            ("CPU", info.Cpu),
            ("Cores / threads", cores > 0 ? $"{cores} / {(threads > 0 ? threads : cores)}" : null),
            ("Boost clock reached", SensorStats(run, "CPU Max", "Clock") is { } boost ? $"{F(boost.Max)} MHz" : null),
            ("Package power", Range("CPU Package", "Power", "W")),
            ("Temperature", Range("CPU Package (Tctl/Tdie)", "Temperature", "°C")));

        Section("Memory",
            ("System memory", info.SystemRam),
            ("In use", Range("RAM Used", "Data", "GB")),
            ("Used by the game", Range("RAM Game Used", "Data", "GB")),
            ("Usage", extra.RamUsageAvgPct is { } a ? $"{F(a)}% on average, up to {F(extra.RamUsageMaxPct ?? a)}%" : null),
            ("Virtual memory (commit)", extra.VirtualMemAvgPct is { } c ? $"{F(c)}% on average, up to {F(extra.VirtualMemMaxPct ?? c)}%" : null));

        Section("Display",
            ("Game resolution", info.Resolution),
            ("Refresh rate seen", extra.RefreshMaxHz is { } hi ? $"{F(extra.RefreshMinHz ?? hi)} to {F(hi)} Hz" : null),
            ("Variable refresh", extra.RefreshMaxHz is null ? null : extra.VrrEvidence ? "In use (refresh rate moved during the capture)" : "No sign of it (refresh rate stayed fixed)"),
            ("Windows display mode", system.Get("display_mode")),
            ("Monitors connected", system.Get("monitor_count")),
            ("Presentation mode", info.PresentationMode),
            ("Display layer", info.DisplayLayer));

        Section("Board and Windows",
            ("Motherboard", info.Motherboard),
            ("Windows", info.Os),
            ("Game Mode", info.GameMode.Length > 0 ? info.GameMode : system.Get("game_mode")),
            ("Hardware-accelerated GPU scheduling", info.Hags.Length > 0 ? info.Hags : system.Get("hags")),
            ("Power plan", system.Get("power_plan")),
            ("Variable refresh for windowed games", system.Get("windowed_vrr")),
            ("Captured with", info.CaptureToolVersion.Length > 0 ? "CapFrameX " + info.CaptureToolVersion : null));

        var driveSize = system.Get("game_drive.total_gb");
        Section("Where the game is installed",
            ("Drive type", system.Get("game_drive.media") is { } media && system.Get("game_drive.bus") is { } bus ? $"{media} ({bus})" : null),
            ("Drive model", system.Get("game_drive.model")),
            ("Size / free", driveSize is not null ? $"{driveSize} GB, {system.Get("game_drive.free_gb")} GB free ({system.Get("game_drive.free_pct")}%)" : null),
            ("File system", system.Get("game_drive.filesystem")),
            ("Driver shader cache", system.Get("shader_cache_mb") is { } mb ? $"{mb} MB in {system.Get("shader_cache_files")} files" : null));

        var user = system.Items.Where(i => i.Source == SettingSource.User).Select(i => (i.Label, (string?)i.Value)).ToArray();
        if (user.Length > 0)
        {
            Section("Entered by you", user);
        }

        return new PcSpecs(summary, sections);
    }

    private static SeriesSummary? SensorStats(CaptureRun run, string name, string type)
    {
        var sensor = run.FindSensor(name, type);
        if (sensor is null)
        {
            return null;
        }

        var s = Stats.Summarize(sensor.Values);
        return s.Count == 0 ? null : s;
    }

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^Core #\d+$")]
    private static partial Regex CoreName();

    [GeneratedRegex(@"^Core #\d+\s+Thread #\d+$")]
    private static partial Regex ThreadName();
}
