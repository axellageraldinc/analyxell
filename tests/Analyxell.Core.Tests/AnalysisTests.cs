using Analyxell.Core.Analysis;
using Analyxell.Core.Capture;
using Analyxell.Core.Config;

namespace Analyxell.Core.Tests;

public class FrameMetricsTests
{
    [Fact]
    public void Steady_frames_give_exact_fps()
    {
        var m = FrameMetrics.Compute(TestData.Steady(1000, 10));
        Assert.Equal(100, m.AverageFps, 3);
        Assert.Equal(100, m.P1Fps, 3);
        Assert.Equal(100, m.Low1AverageFps, 3);
        Assert.Equal(0, m.FpsStdDev, 3);
    }

    [Fact]
    public void One_slow_frame_drags_the_lows_not_the_average_much()
    {
        var m = FrameMetrics.Compute(TestData.Concat(TestData.Steady(99, 10), [100]));
        Assert.Equal(100 / 1.09, m.AverageFps, 2);
        Assert.Equal(10, m.Low1AverageFps, 3);
        Assert.Equal(10, m.MinFps, 3);
    }

    [Fact]
    public void Empty_or_invalid_input_returns_empty_metrics()
    {
        Assert.Equal(0, FrameMetrics.Compute([]).FrameCount);
        Assert.Equal(0, FrameMetrics.Compute([0, -1, double.NaN]).FrameCount);
    }
}

public class SmoothingTests
{
    [Fact]
    public void Bins_are_averaged_and_centred()
    {
        double[] t = [0.1, 0.5, 0.9, 1.2, 1.8, 2.5];
        double[] v = [10, 20, 30, 40, 60, 100];
        var (times, values) = Smoothing.BinAverage(t, v, 1.0);
        Assert.Equal([0.5, 1.5, 2.5], times);
        Assert.Equal([20, 50, 100], values);
    }

    [Fact]
    public void Empty_input_gives_empty_output() => Assert.Empty(Smoothing.BinAverage([], []).Times);
}

public class DetectorTests
{
    [Fact]
    public void Finds_a_spike_and_ignores_normal_jitter()
    {
        var frames = TestData.Concat(TestData.Steady(200, 10), [60], TestData.Steady(200, 10));
        var stutters = Detectors.FindStutters(TestData.Times(frames), frames);
        var s = Assert.Single(stutters);
        Assert.Equal(60, s.FrameMs);
        Assert.Equal(6, s.Severity, 3);

        var jitter = Enumerable.Range(0, 400).Select(i => 10 + (i % 3)).Select(x => (double)x).ToArray();
        Assert.Empty(Detectors.FindStutters(TestData.Times(jitter), jitter));
    }

    [Fact]
    public void Small_spikes_below_the_minimum_are_ignored()
    {
        var frames = TestData.Concat(TestData.Steady(200, 2), [6], TestData.Steady(200, 2));
        Assert.Empty(Detectors.FindStutters(TestData.Times(frames), frames));
    }

    [Fact]
    public void Freezes_use_an_absolute_threshold()
    {
        var frames = TestData.Concat(TestData.Steady(10, 10), [199, 200, 800]);
        var freezes = Detectors.FindFreezes(TestData.Times(frames), frames, 200);
        Assert.Equal([200, 800], freezes.Select(f => f.FrameMs));
    }
}

public class BottleneckTests
{
    private static CaptureRun WithLoads(double gpu, double cpuMax)
    {
        var grid = TestData.SensorGrid(10);
        return TestData.Run(TestData.Steady(1000, 10),
        [
            TestData.Sensor("g", "GPU Core", "Load", TestData.Const(grid, gpu)),
            TestData.Sensor("c", "CPU Max", "Load", TestData.Const(grid, cpuMax)),
        ], grid);
    }

    [Fact]
    public void High_gpu_load_is_gpu_bound() =>
        Assert.Equal(BottleneckVerdict.GpuBound, BottleneckAnalyzer.Analyze(WithLoads(98, 60)).Verdict);

    [Fact]
    public void Busy_cpu_thread_with_idle_gpu_is_cpu_bound() =>
        Assert.Equal(BottleneckVerdict.CpuBound, BottleneckAnalyzer.Analyze(WithLoads(60, 95)).Verdict);

    [Fact]
    public void Nothing_saturated_is_balanced() =>
        Assert.Equal(BottleneckVerdict.Balanced, BottleneckAnalyzer.Analyze(WithLoads(70, 70)).Verdict);

    [Fact]
    public void No_sensor_data_is_unknown() =>
        Assert.Equal(BottleneckVerdict.Unknown, BottleneckAnalyzer.Analyze(TestData.Run(TestData.Steady(100, 10))).Verdict);

    [Fact]
    public void Thermal_limit_beats_everything_else()
    {
        var grid = TestData.SensorGrid(10);
        var run = TestData.Run(TestData.Steady(1000, 10),
        [
            TestData.Sensor("g", "GPU Core", "Load", TestData.Const(grid, 99)),
            TestData.Sensor("t", "GPU Thermal Limit", "Factor", TestData.Const(grid, 1)),
        ], grid);
        Assert.Equal(BottleneckVerdict.ThermalThrottling, BottleneckAnalyzer.Analyze(run).Verdict);
    }
}

public class RuleTests
{
    private static AnalysisResult Analyze(CaptureRun run, GameSettings? settings = null) =>
        CaptureAnalyzer.Analyze(TestData.File(run), 0, settings);

    [Fact]
    public void Freezes_with_busy_gpu_point_away_from_the_system_and_rule_out_disk()
    {
        var r = Analyze(TestData.RunWithFreezes(gpuLoad: 95, driveActivityNearFreeze: 10));
        Assert.Equal(2, r.Freezes.Count);
        Assert.Contains(r.Findings, f => f.Id == "FREEZES");
        Assert.Contains(r.Findings, f => f.Id == "FREEZES_GPU_BUSY");
        Assert.Contains(r.Findings, f => f.Id == "DISK_RULED_OUT");
        Assert.DoesNotContain(r.Findings, f => f.Id == "DISK_ACTIVITY_AT_FREEZES");
    }

    [Fact]
    public void Freezes_with_idle_gpu_and_disk_spike_are_flagged()
    {
        var r = Analyze(TestData.RunWithFreezes(gpuLoad: 20, driveActivityNearFreeze: 95));
        Assert.Contains(r.Findings, f => f.Id == "FREEZES_GPU_IDLE");
        Assert.Contains(r.Findings, f => f.Id == "DISK_ACTIVITY_AT_FREEZES");
        Assert.DoesNotContain(r.Findings, f => f.Id == "DISK_RULED_OUT");
    }

    [Fact]
    public void Smooth_capture_reports_smooth_and_no_freezes()
    {
        var r = Analyze(TestData.Run(TestData.Steady(3000, 10)));
        Assert.Empty(r.Freezes);
        Assert.Contains(r.Findings, f => f.Id == "SMOOTH");
    }

    [Fact]
    public void Every_finding_cites_only_facts_that_exist()
    {
        var r = Analyze(TestData.RunWithFreezes(95, 95));
        Assert.All(r.Findings, f =>
        {
            Assert.NotEmpty(f.Evidence);
            Assert.All(f.Evidence, key => Assert.True(r.Facts.ContainsKey(key), $"{f.Id} cites missing fact '{key}'"));
        });
    }

    [Fact]
    public void Ray_tracing_hypothesis_only_appears_with_rt_on_and_hitches()
    {
        var on = new GameSettings("G", "f.ini", DateTime.UtcNow, [new GameSetting("ray_tracing", "Ray tracing", "On", false)]);
        var off = new GameSettings("G", "f.ini", DateTime.UtcNow, [new GameSetting("ray_tracing", "Ray tracing", "Off", false)]);
        var run = TestData.RunWithFreezes(95, 10);
        Assert.DoesNotContain(Analyze(run, on).Findings, f => f.Id == "RAY_TRACING_WITH_HITCHES");

        var many = TestData.Concat(TestData.Steady(500, 10), [300, 300, 300], TestData.Steady(500, 10));
        Assert.Contains(Analyze(TestData.Run(many), on).Findings, f => f.Id == "RAY_TRACING_WITH_HITCHES");
        Assert.DoesNotContain(Analyze(TestData.Run(many), off).Findings, f => f.Id == "RAY_TRACING_WITH_HITCHES");
    }

    [Fact]
    public void Facts_never_contain_the_source_path()
    {
        var file = TestData.File(TestData.Run(TestData.Steady(100, 10)));
        var withPath = new Analyxell.Core.Capture.CaptureFile
        {
            Info = file.Info,
            Runs = file.Runs,
            SourcePath = @"C:\Users\someone\secret\capture.json",
        };
        var r = CaptureAnalyzer.Analyze(withPath);
        Assert.DoesNotContain(r.Facts.Values, v => v.Contains("someone") || v.Contains("secret"));
    }
}

public class ComparisonTests
{
    [Fact]
    public void Improvement_is_detected_in_the_right_direction()
    {
        var bad = CaptureAnalyzer.Analyze(TestData.File(TestData.RunWithFreezes(95, 10)));
        var good = CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(6000, 8))));
        var cmp = RunComparer.Compare(bad, good);

        Assert.True(cmp.Rows.Single(r => r.Key == "fps.average").Improved);
        Assert.True(cmp.Rows.Single(r => r.Key == "freeze.count").Improved);
        Assert.Equal(0, cmp.WorsenedCount);
        Assert.Equal("2", cmp.Facts["a.freeze.count"]);
        Assert.Equal("0", cmp.Facts["b.freeze.count"]);
    }

    [Fact]
    public void Context_rows_are_informational_and_not_counted()
    {
        var a = CaptureAnalyzer.Analyze(TestData.File(TestData.RunWithFreezes(95, 10)));
        var b = CaptureAnalyzer.Analyze(TestData.File(TestData.RunWithFreezes(60, 10)));
        var cmp = RunComparer.Compare(a, b);

        var gpu = cmp.Rows.Single(r => r.Key == "gpu.load_avg");
        Assert.Null(gpu.HigherIsBetter);
        Assert.Null(gpu.Improved);
        Assert.Equal(6, cmp.ComparedCount);
    }

    [Fact]
    public void Differences_under_one_percent_are_not_called_changes()
    {
        var row = new ComparisonRow("k", "K", 100, 100.5, true, "");
        Assert.Null(row.Improved);
    }
}
