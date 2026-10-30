using System.Globalization;

namespace Analyxell.Core.Analysis;

/// <summary>A one-glance answer to "how did this run go?", shown above the details.</summary>
public sealed record OverallVerdict(Severity Level, string Headline, string Detail)
{
    public static OverallVerdict From(AnalysisResult r)
    {
        var worst = r.Findings.Count == 0 ? Severity.Info : r.Findings.Max(f => f.Severity);
        var headline = worst switch
        {
            Severity.High => "Serious problems found",
            Severity.Medium => "Some things are worth fixing",
            Severity.Low => "Mostly fine, with a few small things",
            _ => "This run looks healthy",
        };

        var parts = new List<string>
        {
            $"{F(r.Metrics.AverageFps)} fps on average, {F(r.Metrics.Low1AverageFps)} fps in the worst 1% of frames.",
        };

        parts.Add(r.Freezes.Count switch
        {
            0 => "No freezes.",
            1 => "1 freeze of 200 ms or more.",
            var n => $"{n} freezes of 200 ms or more.",
        });

        var limit = r.Bottleneck.Verdict switch
        {
            BottleneckVerdict.GpuBound => "The GPU is the limit.",
            BottleneckVerdict.CpuBound => "The CPU is the limit.",
            BottleneckVerdict.ThermalThrottling => "Heat is holding performance back.",
            BottleneckVerdict.PowerLimited => "A power limit is holding performance back.",
            _ => "",
        };
        if (limit.Length > 0)
        {
            parts.Add(limit);
        }

        if (r.NextSteps.Count > 0 && worst >= Severity.Low)
        {
            parts.Add("Start with step 1 below.");
        }

        return new OverallVerdict(worst, headline, string.Join(" ", parts));
    }

    private static string F(double v) => v.ToString("0", CultureInfo.InvariantCulture);
}
