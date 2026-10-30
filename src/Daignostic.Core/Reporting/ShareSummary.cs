using System.Globalization;
using System.Text;
using Daignostic.Core.Analysis;

namespace Daignostic.Core.Reporting;

/// <summary>
/// A short text summary to paste into a forum or chat when asking for help. It holds the game, the numbers, the
/// hardware model names and the main findings; never a computer name, file path, motherboard or key.
/// </summary>
public static class ShareSummary
{
    public static string Build(AnalysisResult r)
    {
        var m = r.Metrics;
        var verdict = OverallVerdict.From(r);
        var sb = new StringBuilder();
        var game = r.Info.Game.Length > 0 ? r.Info.Game : "Unknown game";
        var minutes = m.DurationSec / 60;
        sb.AppendLine($"**{game}**, {F(minutes)} min, {r.Info.GraphicsApi} {r.Info.Resolution}".TrimEnd());

        var fg = r.Facts.GetValueOrDefault("framegen.detected") == "yes" && r.Facts.TryGetValue("framegen.rendered_fps", out var rendered)
            ? $", {rendered} fps really rendered (frame generation)"
            : "";
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"{F(m.AverageFps)} fps average, 1% low {F(m.Low1AverageFps)}, 0.1% low {F(m.Low01AverageFps)}, {r.Freezes.Count} freeze(s), {r.Stutters.Count} stutter(s){fg}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Limited by: {Limit(r.Bottleneck.Verdict)} ({r.Bottleneck.Confidence.ToString().ToLowerInvariant()} confidence)");

        var hardware = new[] { r.Info.Gpu, r.Info.Cpu, r.Info.SystemRam }.Where(s => !string.IsNullOrWhiteSpace(s));
        sb.AppendLine("PC: " + string.Join(" / ", hardware));
        sb.AppendLine();
        sb.AppendLine($"{verdict.Headline}. {verdict.Detail}");

        var attention = r.Findings.Where(f => f.Severity >= Severity.Low && !f.LocalOnly).Take(5).ToList();
        if (attention.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("What stands out:");
            foreach (var f in attention)
            {
                sb.AppendLine($"- {f.Title}");
            }
        }

        if (r.NextSteps.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("To try first:");
            foreach (var s in r.NextSteps.Take(3))
            {
                sb.AppendLine($"{s.Order}. {s.Text}");
            }
        }

        sb.AppendLine();
        sb.AppendLine("Analysed with daignostic (read-only, local).");
        return sb.ToString().TrimEnd();
    }

    private static string Limit(BottleneckVerdict v) => v switch
    {
        BottleneckVerdict.GpuBound => "the GPU",
        BottleneckVerdict.CpuBound => "the CPU",
        BottleneckVerdict.ThermalThrottling => "heat",
        BottleneckVerdict.PowerLimited => "a power limit",
        BottleneckVerdict.Balanced => "nothing in particular",
        _ => "unknown",
    };

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
