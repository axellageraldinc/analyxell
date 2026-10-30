using Analyxell.Core.Capture;

namespace Analyxell.Core.Analysis;

public enum BottleneckVerdict
{
    Unknown,
    GpuBound,
    CpuBound,
    Balanced,
    ThermalThrottling,
    PowerLimited,
}

public enum Confidence
{
    Low,
    Medium,
    High,
}

public sealed record BottleneckResult(
    BottleneckVerdict Verdict,
    Confidence Confidence,
    string Reasoning,
    double? GpuBusyPct,
    double? GpuLoadAvg,
    double? CpuMaxThreadLoadAvg,
    double? CpuTotalLoadAvg,
    double? GpuTempMax,
    double? GpuHotSpotMax,
    double? CpuTempMax,
    double? PowerLimitHitsPct,
    double? VramUsedMaxGb,
    double? VramTotalGb);

public static class BottleneckAnalyzer
{
    public static BottleneckResult Analyze(CaptureRun run)
    {
        var gpuBusy = GpuBusyPct(run);
        var gpuLoad = Avg(run, "GPU Core", "Load");
        var cpuMax = Avg(run, "CPU Max", "Load");
        var cpuTotal = Avg(run, "CPU Total", "Load");
        var gpuTemp = Max(run, "GPU Core", "Temperature");
        var hotSpot = Max(run, "GPU Hot Spot", "Temperature");
        var cpuTemp = Max(run, "CPU Package (Tctl/Tdie)", "Temperature");
        var powerHits = HitsPct(run, "GPU Power Limit");
        var thermalHits = HitsPct(run, "GPU Thermal Limit");
        var vramUsed = Max(run, "GPU Memory Dedicated", "Data") ?? Max(run, "GPU Memory Used", "Data");
        var vramTotal = Max(run, "GPU Memory Total", "Data");

        BottleneckVerdict verdict;
        Confidence confidence;
        string reasoning;

        if (thermalHits is >= 5 || gpuTemp is >= 87)
        {
            verdict = BottleneckVerdict.ThermalThrottling;
            confidence = thermalHits is >= 5 ? Confidence.High : Confidence.Medium;
            reasoning = $"GPU thermal limit hit in {Fmt(thermalHits)}% of samples, peak {Fmt(gpuTemp)} C.";
        }
        else if (powerHits is >= 25)
        {
            verdict = BottleneckVerdict.PowerLimited;
            confidence = powerHits >= 50 ? Confidence.High : Confidence.Medium;
            reasoning = $"GPU power limit hit in {Fmt(powerHits)}% of samples.";
        }
        else if (gpuBusy is >= 90 || gpuLoad is >= 95)
        {
            verdict = BottleneckVerdict.GpuBound;
            confidence = Confidence.High;
            reasoning = $"GPU is the limit: busy {Fmt(gpuBusy)}% of frame time, load avg {Fmt(gpuLoad)}%.";
        }
        else if (gpuLoad is >= 85 && cpuMax is not null && cpuMax <= gpuLoad - 15)
        {
            verdict = BottleneckVerdict.GpuBound;
            confidence = Confidence.Medium;
            reasoning = $"GPU load avg {Fmt(gpuLoad)}% vs busiest CPU thread {Fmt(cpuMax)}%.";
        }
        else if (cpuMax is >= 90 && (gpuLoad is null or < 85))
        {
            verdict = BottleneckVerdict.CpuBound;
            confidence = Confidence.Medium;
            reasoning = $"Busiest CPU thread avg {Fmt(cpuMax)}% while GPU load is {Fmt(gpuLoad)}%.";
        }
        else if (gpuLoad is null && cpuMax is null && gpuBusy is null)
        {
            verdict = BottleneckVerdict.Unknown;
            confidence = Confidence.Low;
            reasoning = "No GPU or CPU load data in this capture.";
        }
        else
        {
            verdict = BottleneckVerdict.Balanced;
            confidence = Confidence.Low;
            reasoning = $"No clear limit: GPU load {Fmt(gpuLoad)}%, busiest CPU thread {Fmt(cpuMax)}%.";
        }

        return new BottleneckResult(
            verdict, confidence, reasoning, gpuBusy, gpuLoad, cpuMax, cpuTotal,
            gpuTemp, hotSpot, cpuTemp, powerHits, vramUsed, vramTotal);
    }

    private static double? GpuBusyPct(CaptureRun run)
    {
        if (run.GpuActiveMs.Length == 0 || run.GpuActiveMs.Length != run.FrameMs.Length)
        {
            return null;
        }

        var gpu = Stats.Summarize(run.GpuActiveMs);
        var frame = Stats.Summarize(run.FrameMs);
        return gpu.Count == 0 || frame.Count == 0 || frame.Average <= 0
            ? null
            : Math.Min(100.0, gpu.Average / frame.Average * 100.0);
    }

    private static double? Avg(CaptureRun run, string name, string type) => Pick(run, name, type, s => s.Average);

    private static double? Max(CaptureRun run, string name, string type) => Pick(run, name, type, s => s.Max);

    private static double? Pick(CaptureRun run, string name, string type, Func<SeriesSummary, double> select)
    {
        var sensor = run.FindSensor(name, type);
        if (sensor is null)
        {
            return null;
        }

        var summary = Stats.Summarize(sensor.Values);
        return summary.Count == 0 ? null : select(summary);
    }

    private static double? HitsPct(CaptureRun run, string name)
    {
        var sensor = run.FindSensor(name, "Factor");
        if (sensor is null || sensor.Values.Length == 0)
        {
            return null;
        }

        var hits = sensor.Values.Count(v => double.IsFinite(v) && v >= 0.5);
        return hits * 100.0 / sensor.Values.Length;
    }

    private static string Fmt(double? v) => v is null ? "n/a" : v.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
}
