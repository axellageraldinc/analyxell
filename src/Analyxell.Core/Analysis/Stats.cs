namespace Analyxell.Core.Analysis;

public readonly record struct SeriesSummary(double Average, double Min, double Max, int Count)
{
    public static SeriesSummary None { get; } = new(double.NaN, double.NaN, double.NaN, 0);
}

internal static class Stats
{
    /// <summary>Linear-interpolated quantile of an ascending-sorted list.</summary>
    public static double Percentile(IReadOnlyList<double> sortedAscending, double q)
    {
        if (sortedAscending.Count == 0)
        {
            return double.NaN;
        }

        var pos = Math.Clamp(q, 0, 1) * (sortedAscending.Count - 1);
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        var frac = pos - lo;
        return sortedAscending[lo] + (sortedAscending[hi] - sortedAscending[lo]) * frac;
    }

    public static SeriesSummary Summarize(ReadOnlySpan<double> values)
    {
        var sum = 0.0;
        var min = double.MaxValue;
        var max = double.MinValue;
        var count = 0;
        foreach (var v in values)
        {
            if (!double.IsFinite(v))
            {
                continue;
            }

            sum += v;
            min = Math.Min(min, v);
            max = Math.Max(max, v);
            count++;
        }

        return count == 0 ? SeriesSummary.None : new SeriesSummary(sum / count, min, max, count);
    }

    /// <summary>Index range [from, to) of sorted <paramref name="times"/> that lie within [lo, hi].</summary>
    public static (int From, int To) Range(double[] times, double lo, double hi)
    {
        var from = LowerBound(times, lo);
        var to = LowerBound(times, hi);
        while (to < times.Length && times[to] <= hi)
        {
            to++;
        }

        return (from, Math.Max(from, to));
    }

    private static int LowerBound(double[] sorted, double x)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (sorted[mid] < x)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }
}
