using System.Globalization;

namespace Analyxell.Core.Analysis;

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

/// <summary>A game setting (or frame generation state) that differs between the two runs.</summary>
public sealed record SettingChange(string Key, string Label, string A, string B);

public sealed record ComparisonResult(
    string LabelA,
    string LabelB,
    IReadOnlyList<ComparisonRow> Rows,
    IReadOnlyDictionary<string, string> Facts)
{
    /// <summary>What was different between the runs besides the PC: graphics settings and frame generation.</summary>
    public IReadOnlyList<SettingChange> Changes { get; init; } = [];

    /// <summary>True when either run's game settings are unknown, so differences in them cannot be listed.</summary>
    public bool SettingsUnknown { get; init; }

    /// <summary>True when the differences are ones that change the frame rate itself (resolution, upscaling, frame generation, ray tracing, preset).</summary>
    public bool ChangesAffectFrameRate => Changes.Any(c => FrameRateKeys.Contains(c.Key));

    private static readonly HashSet<string> FrameRateKeys = ["dlss", "frame_generation", "ray_tracing", "ray_tracing_level", "render_scale", "resolution", "quality_preset", "vsync", "frame_cap", "framegen.detected"];

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

        // Drive activity: the busiest moment of each drive in both runs, and what it did around the freezes.
        var drivesA = a.Run.FindSensors("Drive Total Activity", "Load").GroupBy(d => FreezeContextBuilder.HardwareOf(d.Id), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var driveB in b.Run.FindSensors("Drive Total Activity", "Load"))
        {
            var hw = FreezeContextBuilder.HardwareOf(driveB.Id);
            if (drivesA.TryGetValue(hw, out var driveA))
            {
                var peakA = driveA.Values.Where(double.IsFinite).DefaultIfEmpty(double.NaN).Max();
                var peakB = driveB.Values.Where(double.IsFinite).DefaultIfEmpty(double.NaN).Max();
                if (double.IsFinite(peakA) && double.IsFinite(peakB))
                {
                    var kind = hw.StartsWith("nvme", StringComparison.OrdinalIgnoreCase) ? "NVMe drive" : "SATA or other drive";
                    var number = hw.Contains('/') ? hw[(hw.IndexOf('/') + 1)..] : hw;
                    rows.Add(new ComparisonRow("drive.peak_activity." + CaptureAnalyzer.Slug(hw), $"{kind} {number} peak activity", peakA, peakB, null, "%"));
                }
            }
        }

        var facts = new Dictionary<string, string>();
        foreach (var (prefix, result) in new[] { ("a.", a), ("b.", b) })
        {
            foreach (var drive in result.FreezeContext?.Drives ?? [])
            {
                var key = prefix + "freeze_ctx." + CaptureAnalyzer.Slug(drive.Label);
                facts[key + ".at_freeze_max"] = drive.AtFreezeMax.ToString("0.#", CultureInfo.InvariantCulture);
                facts[key + ".overall_avg"] = drive.OverallAverage.ToString("0.#", CultureInfo.InvariantCulture);
            }
        }

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

        var changes = new List<SettingChange>();
        if (a.Settings is not null && b.Settings is not null)
        {
            var keys = a.Settings.Items.Select(i => i.Key).Concat(b.Settings.Items.Select(i => i.Key)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                var va = a.Settings.Get(key);
                var vb = b.Settings.Get(key);
                if (!string.Equals(va, vb, StringComparison.OrdinalIgnoreCase))
                {
                    var label = a.Settings.Items.Concat(b.Settings.Items).First(i => i.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Label;
                    changes.Add(new SettingChange(key, label, va ?? "not set", vb ?? "not set"));
                }
            }
        }

        var fgA = a.Facts.GetValueOrDefault("framegen.detected") == "yes";
        var fgB = b.Facts.GetValueOrDefault("framegen.detected") == "yes";
        if (fgA != fgB)
        {
            changes.Add(new SettingChange("framegen.detected", "Frame generation seen in the capture", fgA ? "yes" : "no", fgB ? "yes" : "no"));
        }

        foreach (var c in changes)
        {
            facts[$"setting_changed.{c.Key}"] = $"A: {c.A}, B: {c.B}";
        }

        facts["settings_known_for_both_runs"] = a.Settings is not null && b.Settings is not null ? "yes" : "no";
        return new ComparisonResult(labelA, labelB, rows, facts)
        {
            Changes = changes,
            SettingsUnknown = a.Settings is null || b.Settings is null,
        };
    }
}
