using System.Globalization;
using System.Text.RegularExpressions;
using Daignostic.Core.Capture;

namespace Daignostic.Core.Analysis;

/// <summary>
/// How the run changed from start to end, and how much of it the CPU limited. These are the signals behind
/// "it gets worse the longer you play" and "hitches that fade" (memory filling up, shader compilation).
/// </summary>
public sealed record RunTrends(
    double DurationSec,
    double? FpsFirstQuarter,
    double? FpsLastQuarter,
    int EventsFirstThird,
    int EventsLastThird,
    double? VramFirstQuarterGb,
    double? VramLastQuarterGb,
    double? RamFirstQuarterPct,
    double? RamLastQuarterPct,
    double? GpuTempFirstQuarterC,
    double? GpuTempLastQuarterC,
    double? CpuLimitedPct)
{
    public static RunTrends Compute(CaptureRun run, IReadOnlyList<Stutter> stutters, IReadOnlyList<Freeze> freezes)
    {
        var duration = run.TimeSec.Length > 0 ? run.TimeSec[^1] : 0;
        if (duration < 60)
        {
            // Too short to say anything about change over time.
            return new RunTrends(duration, null, null, 0, 0, null, null, null, null, null, null, CpuLimited(run));
        }

        double Fps(double from, double to)
        {
            var frames = run.TimeSec.Count(t => t >= from && t < to);
            return frames / (to - from);
        }

        var times = stutters.Select(s => s.TimeSec).Concat(freezes.Select(f => f.TimeSec)).ToList();
        var third = duration / 3;

        return new RunTrends(
            duration,
            Fps(0, duration / 4),
            Fps(duration * 0.75, duration + 1),
            times.Count(t => t < third),
            times.Count(t => t >= duration - third),
            Quarter(run, "GPU Memory Dedicated", "Data", first: true) ?? Quarter(run, "GPU Memory Used", "Data", first: true),
            Quarter(run, "GPU Memory Dedicated", "Data", first: false) ?? Quarter(run, "GPU Memory Used", "Data", first: false),
            Quarter(run, "RAM Usage", "Load", first: true),
            Quarter(run, "RAM Usage", "Load", first: false),
            Quarter(run, "GPU Core", "Temperature", first: true),
            Quarter(run, "GPU Core", "Temperature", first: false),
            CpuLimited(run));
    }

    private static double? Quarter(CaptureRun run, string name, string type, bool first)
    {
        var s = run.FindSensor(name, type);
        if (s is null || s.Values.Length < 8 || s.Values.Length != run.SensorTimeSec.Length)
        {
            return null;
        }

        var n = s.Values.Length;
        var slice = first ? s.Values.Take(n / 4) : s.Values.Skip(n - n / 4);
        var values = slice.Where(double.IsFinite).ToArray();
        return values.Length == 0 ? null : values.Average();
    }

    /// <summary>Share of sensor samples where one CPU thread was nearly maxed while the GPU still had room.</summary>
    private static double? CpuLimited(CaptureRun run)
    {
        var cpu = run.FindSensor("CPU Max", "Load");
        var gpu = run.FindSensor("GPU Core", "Load");
        if (cpu is null || gpu is null || cpu.Values.Length == 0 || cpu.Values.Length != gpu.Values.Length)
        {
            return null;
        }

        var valid = 0;
        var limited = 0;
        for (var i = 0; i < cpu.Values.Length; i++)
        {
            if (!double.IsFinite(cpu.Values[i]) || !double.IsFinite(gpu.Values[i]))
            {
                continue;
            }

            valid++;
            if (cpu.Values[i] >= 90 && gpu.Values[i] < 85)
            {
                limited++;
            }
        }

        return valid == 0 ? null : limited * 100.0 / valid;
    }
}

/// <summary>What the capture says about the system memory modules, read from text like "32GB (2x16GB) 3200MT/s".</summary>
public sealed record MemoryConfig(int? Modules, int? SpeedMTs)
{
    private static readonly Regex ModulesPattern = new(@"\((\d+)\s*x\s*\d+\s*GB\)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex SpeedPattern = new(@"(\d{3,5})\s*MT/s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static MemoryConfig Parse(string text)
    {
        var m = ModulesPattern.Match(text ?? "");
        var s = SpeedPattern.Match(text ?? "");
        return new MemoryConfig(
            m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null,
            s.Success ? int.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture) : null);
    }
}
