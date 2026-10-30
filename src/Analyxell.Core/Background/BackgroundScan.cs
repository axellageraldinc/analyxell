using System.Globalization;

namespace Analyxell.Core.Background;

/// <summary>One process as seen by a sampler at an instant. Counters are cumulative since the process started.</summary>
public sealed record ProcessReading(int Pid, string Name, TimeSpan CpuTime, ulong IoReadBytes, ulong IoWriteBytes, long WorkingSetBytes);

public sealed record ProcessSnapshot(DateTimeOffset At, IReadOnlyList<ProcessReading> Processes);

/// <summary>Read-only source of process information. The real one lives in <c>WindowsProcessSampler</c>.</summary>
public interface IProcessSampler
{
    int LogicalCores { get; }

    ProcessSnapshot Take();

    /// <summary>Starts measuring per-process GPU use. Returns false when the platform cannot provide it.</summary>
    bool BeginGpu();

    /// <summary>Per-process GPU utilisation (percent, 0-100) since <see cref="BeginGpu"/>, or null.</summary>
    IReadOnlyDictionary<int, double>? EndGpu();
}

public sealed record ProcessUsage(
    string Name,
    int Pid,
    double CpuPct,
    double MemoryMb,
    double DiskMBps,
    double? GpuPct,
    string Category,
    string Advice,
    bool IsGame);

public sealed record BackgroundScanResult(
    DateTimeOffset At,
    double DurationSec,
    IReadOnlyList<ProcessUsage> Processes,
    double OtherCpuPct,
    double OtherDiskMBps,
    double? OtherGpuPct,
    double? GameCpuPct,
    bool GpuMeasured)
{
    public IEnumerable<ProcessUsage> Others => Processes.Where(p => !p.IsGame);

    public IEnumerable<ProcessUsage> KnownInterferers => Others.Where(p => p.Category.Length > 0);

    /// <summary>Anonymous summary of the categories present, safe to share (never contains process names).</summary>
    public string CategorySummary =>
        string.Join(", ", KnownInterferers.Select(p => p.Category).Distinct().Order());
}

/// <summary>Samples running processes for a short time to find programs competing with the game.</summary>
public static class BackgroundScanner
{
    public static async Task<BackgroundScanResult> RunAsync(
        IProcessSampler sampler, TimeSpan duration, string gameProcessName, CancellationToken ct)
    {
        var gpu = sampler.BeginGpu();
        var first = sampler.Take();
        await Task.Delay(duration, ct).ConfigureAwait(false);
        var second = sampler.Take();
        var gpuByPid = gpu ? sampler.EndGpu() : null;
        return Compute(first, second, sampler.LogicalCores, gameProcessName, gpuByPid);
    }

    public static BackgroundScanResult Compute(
        ProcessSnapshot first, ProcessSnapshot second, int logicalCores, string gameProcessName,
        IReadOnlyDictionary<int, double>? gpuByPid)
    {
        var seconds = Math.Max(0.1, (second.At - first.At).TotalSeconds);
        var cores = Math.Max(1, logicalCores);
        var game = NormalizeProcess(gameProcessName);
        var before = first.Processes.GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First());

        var usages = new List<ProcessUsage>();
        foreach (var now in second.Processes)
        {
            if (!before.TryGetValue(now.Pid, out var then) || then.Name != now.Name)
            {
                continue;
            }

            var cpu = Math.Max(0, (now.CpuTime - then.CpuTime).TotalSeconds) / (seconds * cores) * 100.0;
            var io = (Delta(now.IoReadBytes, then.IoReadBytes) + Delta(now.IoWriteBytes, then.IoWriteBytes)) / seconds / (1024.0 * 1024.0);
            double? gpuPct = gpuByPid is not null ? (gpuByPid.TryGetValue(now.Pid, out var g) ? g : 0) : null;
            var known = KnownApps.Find(now.Name);
            var isGame = game.Length > 0 && NormalizeProcess(now.Name) == game;

            if (cpu < 0.5 && io < 0.25 && (gpuPct ?? 0) < 0.5 && known is null && !isGame)
            {
                continue;
            }

            usages.Add(new ProcessUsage(
                now.Name, now.Pid, cpu, now.WorkingSetBytes / (1024.0 * 1024.0), io, gpuPct,
                known?.Category ?? "", known?.Advice ?? "", isGame));
        }

        var others = usages.Where(u => !u.IsGame).ToList();
        var ordered = usages
            .OrderByDescending(u => u.IsGame)
            .ThenByDescending(u => u.CpuPct + u.DiskMBps + (u.GpuPct ?? 0))
            .Take(25)
            .ToList();

        return new BackgroundScanResult(
            second.At, seconds, ordered,
            others.Sum(u => u.CpuPct),
            others.Sum(u => u.DiskMBps),
            gpuByPid is null ? null : others.Sum(u => u.GpuPct ?? 0),
            usages.FirstOrDefault(u => u.IsGame)?.CpuPct,
            gpuByPid is not null);
    }

    public static string NormalizeProcess(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        return n.EndsWith(".exe", StringComparison.Ordinal) ? n[..^4] : n;
    }

    private static double Delta(ulong now, ulong then) => now >= then ? now - then : 0;

    public static string Format(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
