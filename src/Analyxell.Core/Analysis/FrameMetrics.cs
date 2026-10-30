namespace Analyxell.Core.Analysis;

/// <summary>Frame-rate statistics derived from frame times in milliseconds.</summary>
public sealed record FrameMetrics(
    int FrameCount,
    double DurationSec,
    double AverageFps,
    double MedianFps,
    double P1Fps,
    double P02Fps,
    double P01Fps,
    double Low1AverageFps,
    double Low01AverageFps,
    double MinFps,
    double MaxFps,
    double FpsStdDev)
{
    public static FrameMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static FrameMetrics Compute(ReadOnlySpan<double> frameMs)
    {
        var fps = new List<double>(frameMs.Length);
        var totalMs = 0.0;
        foreach (var ms in frameMs)
        {
            if (ms > 0 && double.IsFinite(ms))
            {
                fps.Add(1000.0 / ms);
                totalMs += ms;
            }
        }

        if (fps.Count == 0)
        {
            return Empty;
        }

        var average = fps.Count / (totalMs / 1000.0);
        fps.Sort();

        var variance = 0.0;
        foreach (var f in fps)
        {
            variance += (f - average) * (f - average);
        }

        return new FrameMetrics(
            FrameCount: fps.Count,
            DurationSec: totalMs / 1000.0,
            AverageFps: average,
            MedianFps: Stats.Percentile(fps, 0.5),
            P1Fps: Stats.Percentile(fps, 0.01),
            P02Fps: Stats.Percentile(fps, 0.002),
            P01Fps: Stats.Percentile(fps, 0.001),
            Low1AverageFps: LowAverage(fps, 0.01),
            Low01AverageFps: LowAverage(fps, 0.001),
            MinFps: fps[0],
            MaxFps: fps[^1],
            FpsStdDev: Math.Sqrt(variance / fps.Count));
    }

    /// <summary>
    /// FPS equivalent of the mean frame time of the slowest <paramref name="fraction"/> of frames
    /// (input sorted ascending by FPS). This is the definition CapFrameX uses for its "low average" metrics.
    /// </summary>
    private static double LowAverage(List<double> sortedFps, double fraction)
    {
        var count = Math.Max(1, (int)Math.Ceiling(sortedFps.Count * fraction));
        var sumMs = 0.0;
        for (var i = 0; i < count; i++)
        {
            sumMs += 1000.0 / sortedFps[i];
        }

        return 1000.0 / (sumMs / count);
    }
}
