namespace Daignostic.Core.Analysis;

/// <summary>A frame whose time spiked relative to its neighbours.</summary>
public sealed record Stutter(double TimeSec, double FrameMs, double MedianMs, double Severity);

/// <summary>A frame that took longer than an absolute threshold (a real stall).</summary>
public sealed record Freeze(double TimeSec, double FrameMs);

public static class Detectors
{
    /// <summary>
    /// Finds frame-time spikes: frames slower than <paramref name="factor"/> times the rolling median
    /// of the surrounding <paramref name="window"/> frames and at least <paramref name="minMs"/> long.
    /// </summary>
    public static IReadOnlyList<Stutter> FindStutters(
        double[] timeSec, double[] frameMs, double factor = 2.0, double minMs = 8.0, int window = 51)
    {
        var result = new List<Stutter>();
        var n = Math.Min(timeSec.Length, frameMs.Length);
        if (n == 0)
        {
            return result;
        }

        var half = Math.Max(1, window / 2);
        var buffer = new double[(half * 2) + 1];
        for (var i = 0; i < n; i++)
        {
            var ms = frameMs[i];
            if (!double.IsFinite(ms) || ms < minMs)
            {
                continue;
            }

            var start = Math.Max(0, i - half);
            var end = Math.Min(n - 1, i + half);
            var len = end - start + 1;
            Array.Copy(frameMs, start, buffer, 0, len);
            var slice = buffer.AsSpan(0, len);
            slice.Sort();
            var median = slice[len / 2];
            if (median > 0 && ms > factor * median)
            {
                result.Add(new Stutter(timeSec[i], ms, median, ms / median));
            }
        }

        return result;
    }

    public static IReadOnlyList<Freeze> FindFreezes(double[] timeSec, double[] frameMs, double thresholdMs = 200.0)
    {
        var result = new List<Freeze>();
        var n = Math.Min(timeSec.Length, frameMs.Length);
        for (var i = 0; i < n; i++)
        {
            if (double.IsFinite(frameMs[i]) && frameMs[i] >= thresholdMs)
            {
                result.Add(new Freeze(timeSec[i], frameMs[i]));
            }
        }

        return result;
    }
}
