using Analyxell.Core.Capture;

namespace Analyxell.Core.Analysis;

/// <summary>
/// How long video memory sat nearly full. Texture pop-in is visual, so a frame-time capture cannot see it; what it can
/// show is the usual cause: a card whose memory is full has to stream in lower-detail textures and swap the sharp
/// ones in late. A single peak says little (see <c>VRAM_PRESSURE</c>), so this measures how much of the run it lasted.
/// </summary>
/// <param name="SharePct">Share of the sensor samples with video memory at or above <see cref="VramFull.FullFraction"/> of the card.</param>
/// <param name="PeakPct">The highest use as a share of the card's memory.</param>
/// <param name="Spans">The stretches (at least <see cref="VramFull.MinSpanSec"/> long) where it was nearly full, for the chart.</param>
public sealed record VramFullSignal(double SharePct, double PeakPct, IReadOnlyList<ChartEvent> Spans);

public static class VramFull
{
    /// <summary>Video memory at or above this share of the card counts as nearly full.</summary>
    public const double FullFraction = 0.90;

    /// <summary>The finding needs the memory to be nearly full for at least this share of the run.</summary>
    public const double MinSharePct = 20;

    public const double MinSpanSec = 2;

    public static VramFullSignal? Compute(CaptureRun run)
    {
        var used = run.FindSensor("GPU Memory Dedicated", "Data") ?? run.FindSensor("GPU Memory Used", "Data");
        var totalSeries = run.FindSensor("GPU Memory Total", "Data");
        if (used is null || totalSeries is null || used.Values.Length < 8 || used.Values.Length != run.SensorTimeSec.Length)
        {
            return null;
        }

        var totalStats = Stats.Summarize(totalSeries.Values);
        if (totalStats.Count == 0 || totalStats.Max <= 0)
        {
            return null;
        }

        var total = totalStats.Max;
        var valid = 0;
        var full = 0;
        var peak = 0.0;
        var spans = new List<ChartEvent>();
        double? start = null;
        var last = 0.0;
        for (var i = 0; i < used.Values.Length; i++)
        {
            var v = used.Values[i];
            if (!double.IsFinite(v))
            {
                continue;
            }

            valid++;
            peak = Math.Max(peak, v / total);
            if (v / total >= FullFraction)
            {
                full++;
                start ??= run.SensorTimeSec[i];
                last = run.SensorTimeSec[i];
            }
            else if (start is not null)
            {
                AddSpan(spans, start.Value, last);
                start = null;
            }
        }

        if (start is not null)
        {
            AddSpan(spans, start.Value, last);
        }

        if (valid == 0)
        {
            return null;
        }

        return new VramFullSignal(full * 100.0 / valid, peak * 100, spans.Where(s => s.EndSec - s.StartSec >= MinSpanSec).ToList());

        static void AddSpan(List<ChartEvent> list, double from, double to)
        {
            // Short dips below the line (under a second) do not end a stretch.
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
