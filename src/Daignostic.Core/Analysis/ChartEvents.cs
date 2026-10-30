using Daignostic.Core.Capture;

namespace Daignostic.Core.Analysis;

/// <summary>The kinds of moments the chart can mark. Each one is switched on and off by the user.</summary>
public enum EventKind
{
    Freezes,
    Stutters,
    FpsDrops,
    DroppedFrames,
    TearingPossible,
}

/// <summary>A moment (start equals end) or a stretch of the run, in seconds.</summary>
public sealed record ChartEvent(double StartSec, double EndSec);

/// <summary>All the moments of one kind, with the words the UI shows for them.</summary>
public sealed record EventLayer(EventKind Kind, string Label, string Meaning, IReadOnlyList<ChartEvent> Events, string? Detail = null)
{
    public int Count => Events.Count;

    /// <summary>What follows the label on its switch: how many moments, or how much of the run for whole-run conditions.</summary>
    public string CountText => Detail ?? Count.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public bool IsSpan => Kind is EventKind.FpsDrops or EventKind.TearingPossible;
}

/// <summary>Finds the moments worth marking on the chart. Everything comes from the capture; nothing is guessed.</summary>
public static class ChartEventBuilder
{
    /// <summary>A one-second stretch counts as an FPS drop when its average falls this far below the run's typical second.</summary>
    public const double DropFraction = 0.75;

    public static IReadOnlyList<EventLayer> Build(CaptureRun run)
    {
        var layers = new List<EventLayer>
        {
            Freezes(Detectors.FindFreezes(FrameGenerationSignal.SeriesFor(run).Times, FrameGenerationSignal.SeriesFor(run).Ms).Select(f => f.TimeSec)),
            Stutters(StutterTimes(run)),
            FpsDrops(run),
        };

        var dropped = Dropped(run);
        if (dropped.Count > 0)
        {
            layers.Add(new EventLayer(EventKind.DroppedFrames, "Dropped frames",
                "Frames the game made but the display never showed.", dropped));
        }

        var tearing = Tearing(run);
        if (tearing.Count > 0)
        {
            var total = run.TimeSec.Length > 0 ? run.TimeSec[^1] : 0;
            var covered = total > 0 ? Math.Min(100, tearing.Sum(t => t.EndSec - t.StartSec) / total * 100) : 0;
            layers.Add(new EventLayer(EventKind.TearingPossible, "Tearing possible",
                (covered >= 90
                    ? "Tearing was allowed for almost the whole run (VSync off or unsynchronised), shown as a bar along the bottom."
                    : "Stretches where frames were sent to the screen with tearing allowed (VSync off), shown as a bar along the bottom.")
                + " A capture cannot see tearing itself, only whether it could happen.",
                tearing, $"{covered:0}% of the run"));
        }

        return layers;
    }

    /// <summary>Stutters judged the same way as in the analysis, so the chart marks exactly what is counted.</summary>
    private static IEnumerable<double> StutterTimes(CaptureRun run)
    {
        var fg = FrameGenerationSignal.Compute(run);
        var series = FrameGenerationSignal.SeriesFor(run, fg);
        var (times, ms) = series.FromDisplay ? (series.Times, series.Ms) : fg.Merge(series.Times, series.Ms);
        return Detectors.FindStutters(times, ms).Select(s => s.TimeSec);
    }

    public static EventLayer Freezes(IEnumerable<double> times) => new(
        EventKind.Freezes, "Freezes", "Frames of 200 ms or more: the game visibly hung.",
        times.Select(t => new ChartEvent(t, t)).ToList());

    public static EventLayer Stutters(IEnumerable<double> times) => new(
        EventKind.Stutters, "Stutters", "Frames that took more than twice as long as their neighbours.",
        times.Select(t => new ChartEvent(t, t)).ToList());

    public static EventLayer FpsDrops(CaptureRun run)
    {
        var series = FrameGenerationSignal.SeriesFor(run);
        var fps = series.Ms.Select(ms => ms > 0 && double.IsFinite(ms) ? 1000.0 / ms : double.NaN).ToArray();
        var (times, values) = Smoothing.BinAverage(series.Times, fps);
        var spans = new List<ChartEvent>();
        if (values.Length >= 4)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            var typical = sorted[sorted.Length / 2];
            var limit = Math.Min(typical * DropFraction, typical - 5);
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] >= limit)
                {
                    continue;
                }

                var start = times[i] - 0.5;
                var end = times[i] + 0.5;
                if (spans.Count > 0 && start - spans[^1].EndSec < 0.01)
                {
                    spans[^1] = spans[^1] with { EndSec = end };
                }
                else
                {
                    spans.Add(new ChartEvent(start, end));
                }
            }
        }

        return new EventLayer(EventKind.FpsDrops, "FPS drops",
            "Seconds where the average frame rate fell more than a quarter below the run's typical second.", spans);
    }

    private static List<ChartEvent> Dropped(CaptureRun run)
    {
        var result = new List<ChartEvent>();
        var n = Math.Min(run.Dropped.Length, run.TimeSec.Length);
        for (var i = 0; i < n; i++)
        {
            if (run.Dropped[i])
            {
                result.Add(new ChartEvent(run.TimeSec[i], run.TimeSec[i]));
            }
        }

        return result;
    }

    /// <summary>Stretches of at least half a second where tearing was allowed; short gaps are bridged.</summary>
    private static List<ChartEvent> Tearing(CaptureRun run)
    {
        var spans = new List<ChartEvent>();
        var n = Math.Min(run.AllowsTearing.Length, run.TimeSec.Length);
        double? start = null;
        var last = 0.0;
        for (var i = 0; i < n; i++)
        {
            var on = run.AllowsTearing[i] >= 0.5;
            if (on)
            {
                start ??= run.TimeSec[i];
                last = run.TimeSec[i];
            }
            else if (start is not null)
            {
                Add(spans, start.Value, last);
                start = null;
            }
        }

        if (start is not null)
        {
            Add(spans, start.Value, last);
        }

        return spans.Where(s => s.EndSec - s.StartSec >= 0.5).ToList();

        static void Add(List<ChartEvent> list, double from, double to)
        {
            if (list.Count > 0 && from - list[^1].EndSec < 1.0)
            {
                list[^1] = list[^1] with { EndSec = to };
            }
            else
            {
                list.Add(new ChartEvent(from, to));
            }
        }
    }
}
