using Daignostic.Core.Capture;

namespace Daignostic.Core.Tests;

/// <summary>Synthetic captures only. Real captures contain machine details and never belong in the repo.</summary>
internal static class TestData
{
    public static double[] Steady(int count, double ms) => Enumerable.Repeat(ms, count).ToArray();

    public static double[] Concat(params double[][] parts) => parts.SelectMany(p => p).ToArray();

    public static double[] Times(double[] frameMs)
    {
        var t = new double[frameMs.Length];
        var acc = 0.0;
        for (var i = 0; i < frameMs.Length; i++)
        {
            acc += frameMs[i] / 1000.0;
            t[i] = acc;
        }

        return t;
    }

    public static double[] SensorGrid(double durationSec, double stepSec = 0.25)
    {
        var count = (int)(durationSec / stepSec) + 1;
        return Enumerable.Range(0, count).Select(i => i * stepSec).ToArray();
    }

    public static SensorSeries Sensor(string id, string name, string type, double[] values) => new(id, name, type, values);

    public static double[] Const(double[] grid, double value) => grid.Select(_ => value).ToArray();

    public static CaptureRun Run(double[] frameMs, IEnumerable<SensorSeries>? sensors = null, double[]? sensorTimes = null, double[]? gpuActive = null) =>
        new()
        {
            TimeSec = Times(frameMs),
            FrameMs = frameMs,
            GpuActiveMs = gpuActive ?? [],
            SensorTimeSec = sensorTimes ?? [],
            Sensors = sensors?.ToList() ?? [],
        };

    public static CaptureFile File(CaptureRun run, string game = "Test Game", string process = "game.exe") => new()
    {
        Info = new CaptureInfo(game, process, "Test CPU", "Test GPU", "1.0", "1920x1080", "DX12",
            "Fullscreen", "Test OS", "16GB", "2026-01-01T00:00:00Z", "1.0"),
        Runs = [run],
    };

    /// <summary>Three minutes at 100 fps with two 300 ms freezes, at about 20 s and 40 s.</summary>
    public static CaptureRun RunWithFreezes(double gpuLoad, double driveActivityNearFreeze)
    {
        var frames = Concat(Steady(2000, 10), [300], Steady(2000, 10), [300], Steady(2000, 10));
        var grid = SensorGrid(Times(frames)[^1] + 1);
        var drive = Const(grid, 3);
        for (var i = 0; i < grid.Length; i++)
        {
            if (Math.Abs(grid[i] - 20.3) < 0.6)
            {
                drive[i] = driveActivityNearFreeze;
            }
        }

        var sensors = new[]
        {
            Sensor("/gpu-nvidia/0/load/0", "GPU Core", "Load", Const(grid, gpuLoad)),
            Sensor("/amdcpu/0/load/1", "CPU Max", "Load", Const(grid, 50)),
            Sensor("/nvme/1/load/33", "Drive Total Activity", "Load", drive),
        };
        return Run(frames, sensors, grid);
    }
}
