using Analyxell.Core.Capture;

namespace Analyxell.Core.Analysis;

/// <summary>How a sensor behaved around the freezes compared with the whole run.</summary>
public sealed record SensorAtFreeze(
    string Label,
    string Type,
    double OverallAverage,
    double OverallMax,
    double AtFreezeAverage,
    double AtFreezeMax);

public sealed record FreezeContext(int FreezeCount, double HalfWindowSec, IReadOnlyList<SensorAtFreeze> Sensors)
{
    public SensorAtFreeze? Find(string label) =>
        Sensors.FirstOrDefault(s => s.Label.Equals(label, StringComparison.OrdinalIgnoreCase));

    /// <summary>Drive activity sensors, labelled "Drive activity (nvme/1)" and so on.</summary>
    public IEnumerable<SensorAtFreeze> Drives => Sensors.Where(s => s.Label.StartsWith("Drive activity", StringComparison.Ordinal));
}

public static class FreezeContextBuilder
{
    public const double DefaultHalfWindowSec = 2.0;

    public static FreezeContext? Build(CaptureRun run, IReadOnlyList<Freeze> freezes, double halfWindowSec = DefaultHalfWindowSec)
    {
        if (freezes.Count == 0 || run.SensorTimeSec.Length == 0)
        {
            return null;
        }

        var wanted = new List<(string Label, SensorSeries Series)>();
        Add(wanted, "GPU load", run.FindSensor("GPU Core", "Load"));
        Add(wanted, "CPU busiest thread", run.FindSensor("CPU Max", "Load"));
        Add(wanted, "CPU total load", run.FindSensor("CPU Total", "Load"));
        Add(wanted, "GPU power", run.FindSensor("GPU Power", "Power"));
        Add(wanted, "RAM usage", run.FindSensor("RAM Usage", "Load"));
        Add(wanted, "Virtual memory usage", run.FindSensor("Virtual RAM Usage", "Load"));
        foreach (var drive in run.FindSensors("Drive Total Activity", "Load"))
        {
            Add(wanted, $"Drive activity ({HardwareOf(drive.Id)})", drive);
        }

        var result = new List<SensorAtFreeze>();
        foreach (var (label, series) in wanted)
        {
            if (series.Values.Length != run.SensorTimeSec.Length)
            {
                continue;
            }

            var overall = Stats.Summarize(series.Values);
            if (overall.Count == 0)
            {
                continue;
            }

            double avgSum = 0, maxOfMax = double.MinValue;
            var windows = 0;
            foreach (var freeze in freezes)
            {
                var (from, to) = Stats.Range(run.SensorTimeSec, freeze.TimeSec - halfWindowSec, freeze.TimeSec + halfWindowSec);
                if (to <= from)
                {
                    continue;
                }

                var window = Stats.Summarize(series.Values.AsSpan(from, to - from));
                if (window.Count == 0)
                {
                    continue;
                }

                avgSum += window.Average;
                maxOfMax = Math.Max(maxOfMax, window.Max);
                windows++;
            }

            if (windows > 0)
            {
                result.Add(new SensorAtFreeze(label, series.Type, overall.Average, overall.Max, avgSum / windows, maxOfMax));
            }
        }

        return new FreezeContext(freezes.Count, halfWindowSec, result);
    }

    private static void Add(List<(string, SensorSeries)> list, string label, SensorSeries? series)
    {
        if (series is not null)
        {
            list.Add((label, series));
        }
    }

    /// <summary>"/nvme/1/load/33" becomes "nvme/1".</summary>
    public static string HardwareOf(string id)
    {
        var parts = id.Trim('/').Split('/');
        return parts.Length >= 2 ? $"{parts[0]}/{parts[1]}" : id;
    }
}
