namespace Daignostic.Core.Analysis;

public static class Smoothing
{
    /// <summary>
    /// Averages <paramref name="values"/> into consecutive time bins of <paramref name="binSec"/> seconds.
    /// Returns the bin centres and the mean of each bin. Empty bins are skipped.
    /// </summary>
    public static (double[] Times, double[] Values) BinAverage(double[] times, double[] values, double binSec = 1.0)
    {
        var n = Math.Min(times.Length, values.Length);
        if (n == 0 || binSec <= 0)
        {
            return ([], []);
        }

        var outT = new List<double>();
        var outV = new List<double>();
        var bin = 0;
        double sum = 0;
        var count = 0;
        for (var i = 0; i < n; i++)
        {
            if (!double.IsFinite(values[i]))
            {
                continue;
            }

            var b = (int)(times[i] / binSec);
            if (b != bin && count > 0)
            {
                outT.Add((bin + 0.5) * binSec);
                outV.Add(sum / count);
                sum = 0;
                count = 0;
            }

            bin = b;
            sum += values[i];
            count++;
        }

        if (count > 0)
        {
            outT.Add((bin + 0.5) * binSec);
            outV.Add(sum / count);
        }

        return (outT.ToArray(), outV.ToArray());
    }
}
