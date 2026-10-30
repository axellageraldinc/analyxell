using System.Globalization;

namespace Daignostic.Core.Analysis;

/// <summary>One compared metric. <see cref="HigherIsBetter"/> decides whether a delta is an improvement; null means the row is informational only.</summary>
public sealed record ComparisonRow(string Key, string Label, double A, double B, bool? HigherIsBetter, string Unit)
{
    public double Delta => B - A;

    public double? PercentDelta => Math.Abs(A) < 1e-9 ? null : Delta / A * 100.0;

    /// <summary>True when B is better, false when worse, null when equal within 1%.</summary>
    public bool? Improved
    {
        get
        {
            if (HigherIsBetter is null)
            {
                return null;
            }

            var scale = Math.Max(Math.Abs(A), Math.Abs(B));
            if (scale < 1e-9 || Math.Abs(Delta) / scale < 0.01)
            {
                return null;
            }

            return HigherIsBetter.Value ? Delta > 0 : Delta < 0;
        }
    }
}

public sealed record ComparisonResult(
    string LabelA,
    string LabelB,
    IReadOnlyList<ComparisonRow> Rows,
    IReadOnlyDictionary<string, string> Facts)
{
    public int ComparedCount => Rows.Count(r => r.HigherIsBetter is not null);

    public int ImprovedCount => Rows.Count(r => r.Improved == true);

    public int WorsenedCount => Rows.Count(r => r.Improved == false);
}

public static class RunComparer
{
    public static ComparisonResult Compare(AnalysisResult a, AnalysisResult b, string labelA = "Run A", string labelB = "Run B")
    {
        var rows = new List<ComparisonRow>
        {
            new("fps.average", "Average FPS", a.Metrics.AverageFps, b.Metrics.AverageFps, true, "fps"),
            new("fps.p1", "1% (P1) FPS", a.Metrics.P1Fps, b.Metrics.P1Fps, true, "fps"),
            new("fps.low1_average", "1% low average", a.Metrics.Low1AverageFps, b.Metrics.Low1AverageFps, true, "fps"),
            new("fps.low0_1_average", "0.1% low average", a.Metrics.Low01AverageFps, b.Metrics.Low01AverageFps, true, "fps"),
            new("stutter.count", "Stutters", a.Stutters.Count, b.Stutters.Count, false, ""),
            new("freeze.count", "Freezes (200 ms or more)", a.Freezes.Count, b.Freezes.Count, false, ""),
        };

        void Optional(string key, string label, double? va, double? vb, bool? higherIsBetter, string unit)
        {
            if (va is { } x && vb is { } y)
            {
                rows.Add(new ComparisonRow(key, label, x, y, higherIsBetter, unit));
            }
        }

        Optional("gpu.load_avg", "GPU load average", a.Bottleneck.GpuLoadAvg, b.Bottleneck.GpuLoadAvg, null, "%");
        Optional("vram.used_max_gb", "Peak VRAM use", a.Bottleneck.VramUsedMaxGb, b.Bottleneck.VramUsedMaxGb, null, "GB");
        Optional("gpu.temp_max_c", "Peak GPU temperature", a.Bottleneck.GpuTempMax, b.Bottleneck.GpuTempMax, null, "\u00B0C");

        var facts = new Dictionary<string, string>();
        foreach (var row in rows)
        {
            facts[$"a.{row.Key}"] = row.A.ToString("0.#", CultureInfo.InvariantCulture);
            facts[$"b.{row.Key}"] = row.B.ToString("0.#", CultureInfo.InvariantCulture);
            if (row.PercentDelta is { } pct)
            {
                facts[$"delta_pct.{row.Key}"] = pct.ToString("0.#", CultureInfo.InvariantCulture);
            }
        }

        facts["a.bottleneck.verdict"] = a.Bottleneck.Verdict.ToString();
        facts["b.bottleneck.verdict"] = b.Bottleneck.Verdict.ToString();
        return new ComparisonResult(labelA, labelB, rows, facts);
    }
}
