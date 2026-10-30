using Analyxell.Core.Capture;

namespace Analyxell.Core.Analysis;

/// <summary>
/// Whether the capture contains frame generation (DLSS, FSR or Intel). Generated frames sit between real ones, so the
/// gaps between presents can alternate long and short on purpose. A plain "twice the median" stutter test reads that
/// alternation as stutter, which is why frame generation has to be recognised first.
///
/// Three signs are used, because tools record frame generation differently:
/// frames the capture labels as not coming from the game, a display that changes about twice as often as the game
/// presents, and frame times that alternate long-short-long-short.
/// </summary>
public sealed record FrameGenerationSignal(
    bool Active,
    double DisplayToPresentRatio,
    double GeneratedFramesPct,
    double Alternation,
    int FramesPerRendered,
    string Reason,
    double RenderedFps = 0,
    double DisplayedFps = 0)
{
    /// <summary>The display showing this many times more frames than the game presented means frames were added.</summary>
    public const double RatioThreshold = 1.6;

    /// <summary>The capture itself labels this share of frames as not coming from the game.</summary>
    public const double GeneratedShareThreshold = 15;

    /// <summary>Lag-1 correlation of frame times below this is a sustained long-short pattern (ordinary runs sit around 0).</summary>
    public const double AlternationThreshold = -0.4;

    public static FrameGenerationSignal None { get; } = new(false, 1, 0, 0, 1, "");

    public static FrameGenerationSignal Compute(CaptureRun run)
    {
        double ratio = 1;
        var n = Math.Min(run.FrameMs.Length, run.DisplayMs.Length);
        if (n >= 600)
        {
            var presented = run.FrameMs.Take(n).Where(v => double.IsFinite(v) && v > 0).ToArray();
            var shown = run.DisplayMs.Take(n).Where(v => double.IsFinite(v) && v > 0).ToArray();
            if (presented.Length >= 600 && shown.Length >= 600)
            {
                // Same stretch of time, so more displayed frames means a smaller average gap between display changes.
                ratio = presented.Average() / shown.Average();
            }
        }

        var generated = 0.0;
        if (run.FrameTypes.Length > 0)
        {
            var labelled = run.FrameTypes.Count(t => t.Length > 0);
            var other = run.FrameTypes.Count(t => t.Length > 0 && !t.Equals("Application", StringComparison.OrdinalIgnoreCase)
                && !t.StartsWith("Not_Set", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("Unspecified", StringComparison.OrdinalIgnoreCase));
            generated = labelled == 0 ? 0 : other * 100.0 / labelled;
        }

        var alternation = LagOneCorrelation(run.FrameMs);
        var labelledInStream = generated >= GeneratedShareThreshold;
        var alternating = alternation <= AlternationThreshold;
        var doubledDisplay = ratio >= RatioThreshold;

        var duration = run.TimeSec.Length > 0 ? run.TimeSec[^1] : 0;
        var presentedFps = duration > 0 ? run.FrameMs.Length / duration : 0;

        if (!labelledInStream && !alternating && !doubledDisplay)
        {
            return new FrameGenerationSignal(false, ratio, generated, alternation, 1, "", presentedFps, presentedFps);
        }

        // Generated frames are inside the present stream when they are labelled or the times alternate; then single
        // frames cannot be judged on their own. A doubled display with an even stream has nothing to merge.
        var perRendered = labelledInStream
            ? (int)Math.Clamp(Math.Round(100.0 / Math.Max(1, 100 - generated)), 2, 4)
            : alternating ? 2 : 1;
        var reason = labelledInStream ? "labelled" : alternating ? "alternating" : "display";

        // What the PC draws against what the screen shows. When the present stream holds the generated frames, the
        // display series is the one that matches what the player sees; when only the display doubles, the stream is
        // the rendered one and the screen shows ratio times more.
        var shownCount = run.DisplayMs.Count(v => double.IsFinite(v) && v > 0);
        double rendered, displayed;
        if (perRendered >= 2)
        {
            rendered = presentedFps / perRendered;
            displayed = duration > 0 && shownCount >= 600 ? shownCount / duration : presentedFps;
        }
        else
        {
            rendered = presentedFps;
            displayed = presentedFps * ratio;
        }

        return new FrameGenerationSignal(true, ratio, generated, alternation, perRendered, reason, rendered, displayed);
    }

    /// <summary>The frame times to judge smoothness on, and where they came from.</summary>
    public sealed record FrameSeries(double[] Times, double[] Ms, bool FromDisplay);

    /// <summary>
    /// With generated frames in the present stream, a "frame" there is either a real one or a generated one presented a
    /// fraction of a millisecond later, so single present gaps say nothing about smoothness (and 0.6 ms gaps read as
    /// 1,600 fps). The gaps between display changes are what the player actually sees, so those are used instead.
    /// </summary>
    public static FrameSeries SeriesFor(CaptureRun run, FrameGenerationSignal fg)
    {
        if (fg.Active && fg.FramesPerRendered >= 2 && run.DisplayMs.Length >= 600 && run.DisplayMs.Length == run.TimeSec.Length)
        {
            var times = new List<double>(run.TimeSec.Length);
            var ms = new List<double>(run.TimeSec.Length);
            for (var i = 0; i < run.TimeSec.Length; i++)
            {
                if (double.IsFinite(run.DisplayMs[i]) && run.DisplayMs[i] > 0)
                {
                    times.Add(run.TimeSec[i]);
                    ms.Add(run.DisplayMs[i]);
                }
            }

            if (ms.Count >= run.TimeSec.Length * 0.9)
            {
                return new FrameSeries([.. times], [.. ms], FromDisplay: true);
            }
        }

        return new FrameSeries(run.TimeSec, run.FrameMs, FromDisplay: false);
    }

    public static FrameSeries SeriesFor(CaptureRun run) => SeriesFor(run, Compute(run));

    /// <summary>Correlation between each frame time and the next, with huge spikes held back so one freeze cannot decide it.</summary>
    public static double LagOneCorrelation(double[] frameMs)
    {
        var values = frameMs.Where(v => double.IsFinite(v) && v > 0).ToArray();
        if (values.Length < 600)
        {
            return 0;
        }

        var sorted = values.OrderBy(v => v).ToArray();
        var cap = sorted[sorted.Length / 2] * 3;
        var x = values.Select(v => Math.Min(v, cap)).ToArray();
        var mean = x.Average();
        double num = 0, den = 0;
        for (var i = 0; i < x.Length; i++)
        {
            den += (x[i] - mean) * (x[i] - mean);
            if (i + 1 < x.Length)
            {
                num += (x[i] - mean) * (x[i + 1] - mean);
            }
        }

        return den <= 1e-9 ? 0 : num / den;
    }

    /// <summary>
    /// Adds up each group of consecutive frames so the long/short alternation disappears, leaving the real pacing.
    /// Without generated frames in the stream the frames are returned as they are.
    /// </summary>
    public (double[] Times, double[] FrameMs) Merge(double[] times, double[] frameMs)
    {
        if (!Active || FramesPerRendered < 2)
        {
            return (times, frameMs);
        }

        var count = Math.Min(times.Length, frameMs.Length) / FramesPerRendered;
        var t = new double[count];
        var ms = new double[count];
        for (var i = 0; i < count; i++)
        {
            double sum = 0;
            for (var k = 0; k < FramesPerRendered; k++)
            {
                var v = frameMs[(i * FramesPerRendered) + k];
                sum += double.IsFinite(v) ? v : 0;
            }

            ms[i] = sum;
            t[i] = times[((i + 1) * FramesPerRendered) - 1];
        }

        return (t, ms);
    }
}
