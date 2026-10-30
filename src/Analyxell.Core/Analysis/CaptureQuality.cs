using System.Globalization;
using Analyxell.Core.Capture;

namespace Analyxell.Core.Analysis;

/// <summary>A reason a capture may not describe the game well, and what to do about it.</summary>
public sealed record QualityIssue(string Id, string Text, string Fix);

/// <summary>
/// Checks the capture itself before trusting what it says. A run recorded in a menu, capped at 60 fps or ten seconds
/// long cannot show how the game performs, and findings built on it would mislead.
/// </summary>
public static class CaptureQuality
{
    private static readonly double[] CommonCaps = [24, 30, 40, 45, 48, 50, 60, 72, 75, 90, 100, 120, 125, 141, 142, 144, 160, 162, 165, 170, 175, 180, 240, 360];

    public static IReadOnlyList<QualityIssue> Assess(CaptureInfo info, CaptureRun run, FrameMetrics metrics, BottleneckResult bottleneck)
    {
        var issues = new List<QualityIssue>();

        if (metrics.DurationSec > 0 && metrics.DurationSec < 60)
        {
            issues.Add(new QualityIssue("SHORT",
                $"This capture is only {metrics.DurationSec:0} seconds long, which is too short to judge the 1% and 0.1% lows or to see anything that builds up.",
                "Record 2 to 5 minutes of normal play, on the same route each time."));
        }
        else if (metrics.DurationSec is >= 60 and < 120)
        {
            issues.Add(new QualityIssue("BRIEF",
                $"This capture is {metrics.DurationSec:0} seconds long. The lows and any change over time are only rough at this length.",
                "Two to five minutes gives much steadier numbers."));
        }

        if (metrics.FrameCount > 0 && metrics.FrameCount < 1000)
        {
            issues.Add(new QualityIssue("FEW_FRAMES", $"Only {metrics.FrameCount:N0} frames were recorded.", "Record a longer run."));
        }

        if (LooksCapped(run, metrics) is { } cap)
        {
            issues.Add(new QualityIssue("CAPPED",
                $"The frame rate sits almost exactly at {cap:0} fps for the whole run. That looks like a frame cap or VSync, which hides how fast the PC could go.",
                "To measure what the PC can do, record once with the cap and VSync off (the game's own cap, the driver's, or RTSS). For smooth play, a cap is fine."));
        }

        if (bottleneck.GpuLoadAvg is { } gpu && bottleneck.CpuTotalLoadAvg is { } cpu && gpu < 30 && cpu < 25)
        {
            issues.Add(new QualityIssue("IDLE",
                $"Both the GPU ({gpu:0}%) and the CPU ({cpu:0}%) were nearly idle. The capture may have been recorded in a menu, a loading screen or with the game paused.",
                "Record while actually playing, in a demanding scene."));
        }

        if (info.PresentationMode.Contains("Composed", StringComparison.OrdinalIgnoreCase) ||
            info.PresentationMode.Equals("Windowed", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new QualityIssue("WINDOWED",
                $"The game was presented as \"{info.PresentationMode}\", through the desktop compositor, which adds latency and can change frame pacing.",
                "Record in fullscreen or the game's borderless mode."));
        }

        if (bottleneck.GpuLoadAvg is null && bottleneck.CpuMaxThreadLoadAvg is null)
        {
            issues.Add(new QualityIssue("NO_SENSORS",
                "This capture has no GPU or CPU load data, so analyxell cannot tell what is limiting the game, or what happened around a freeze.",
                "In CapFrameX, switch on sensor logging (GPU and CPU load, temperatures, clocks) before recording."));
        }

        return issues;
    }

    /// <summary>The cap the run is pinned to, or null. Needs almost every frame at one common rate and almost no spread.</summary>
    internal static double? LooksCapped(CaptureRun run, FrameMetrics metrics)
    {
        if (metrics.FrameCount < 1000 || metrics.MedianFps <= 0)
        {
            return null;
        }

        var cap = CommonCaps.OrderBy(c => Math.Abs(c - metrics.MedianFps)).First();
        if (Math.Abs(cap - metrics.MedianFps) > 1.5)
        {
            return null;
        }

        var target = 1000.0 / cap;
        var near = run.FrameMs.Count(ms => double.IsFinite(ms) && Math.Abs(ms - target) <= target * 0.04);
        return near >= metrics.FrameCount * 0.8 && metrics.Low1AverageFps >= cap * 0.9 ? cap : null;
    }

    /// <summary>One line for the AI, so it does not draw conclusions from a capture that cannot support them.</summary>
    public static string ForFacts(IReadOnlyList<QualityIssue> issues) =>
        string.Join("; ", issues.Select(i => i.Id.ToLower(CultureInfo.InvariantCulture)));
}
