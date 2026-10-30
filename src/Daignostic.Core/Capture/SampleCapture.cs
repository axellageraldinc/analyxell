using Daignostic.Core.Capture;

namespace Daignostic.Core.Capture;

/// <summary>
/// A made-up capture so a new user can see what daignostic does before they have recorded anything. It is generated
/// here, not shipped from a real PC, so it contains no real hardware or personal data.
/// </summary>
public static class SampleCapture
{
    public static CaptureFile Create()
    {
        var random = new Random(42);
        var frames = new List<double>();

        // Four minutes at about 75 fps, with two long freezes and a few spikes, a bit slower toward the end.
        for (var i = 0; i < 20000; i++)
        {
            var baseMs = 12.5 + (i / 20000.0 * 2.0);
            var ms = baseMs + (random.NextDouble() * 3) - 1.5;
            if (i is 6500 or 14200)
            {
                ms = 380;
            }
            else if (i % 700 == 0)
            {
                ms = baseMs * 3.2;
            }

            frames.Add(ms);
        }

        var times = new double[frames.Count];
        double acc = 0;
        for (var i = 0; i < frames.Count; i++)
        {
            acc += frames[i] / 1000.0;
            times[i] = acc;
        }

        var duration = times[^1];
        var grid = Enumerable.Range(0, (int)(duration / 0.5) + 1).Select(i => i * 0.5).ToArray();
        double[] Wave(double mean, double swing) => grid.Select((t, i) => mean + (Math.Sin(i / 9.0) * swing) + ((random.NextDouble() - 0.5) * swing)).ToArray();

        var run = new CaptureRun
        {
            TimeSec = times,
            FrameMs = [.. frames],
            SensorTimeSec = grid,
            Sensors =
            [
                new SensorSeries("/gpu/0/load/0", "GPU Core", "Load", Wave(96, 2)),
                new SensorSeries("/cpu/0/load/1", "CPU Max", "Load", Wave(58, 8)),
                new SensorSeries("/cpu/0/load/2", "CPU Total", "Load", Wave(34, 5)),
                new SensorSeries("/gpu/0/temp/0", "GPU Core", "Temperature", Wave(71, 3)),
                new SensorSeries("/cpu/0/temp/0", "CPU Package (Tctl/Tdie)", "Temperature", Wave(62, 3)),
            ],
        };

        var info = new CaptureInfo(
            "Sample Game", "", "Sample CPU 8-Core", "Sample GPU 12GB", "1.0", "2560x1440", "DX12",
            "Fullscreen Optimized or Borderless", "Windows 11", "32GB (2x16GB) 3200MT/s",
            DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture), "sample");
        return new CaptureFile { Info = info, Runs = [run], SourcePath = "" };
    }
}
