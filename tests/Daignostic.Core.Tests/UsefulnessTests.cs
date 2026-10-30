using Daignostic.Core.Analysis;
using Daignostic.Core.Capture;
using Daignostic.Core.Config;
using Daignostic.Core.Reporting;

namespace Daignostic.Core.Tests;

public class UsefulnessTests
{
    private static AnalysisResult Analyze(CaptureRun run) => CaptureAnalyzer.Analyze(TestData.File(run));

    [Fact]
    public void A_short_capture_is_flagged_and_a_long_one_is_not()
    {
        var short30 = Analyze(TestData.Run(TestData.Steady(3000, 10)));
        var issues = CaptureQuality.Assess(short30.Info, short30.Run, short30.Metrics, short30.Bottleneck);
        Assert.Contains(issues, i => i.Id == "SHORT");

        var random = new Random(3);
        var long5 = Analyze(TestData.Run(Enumerable.Range(0, 30000).Select(_ => 9 + random.NextDouble() * 4).ToArray()));
        Assert.DoesNotContain(CaptureQuality.Assess(long5.Info, long5.Run, long5.Metrics, long5.Bottleneck), i => i.Id is "SHORT" or "BRIEF" or "CAPPED");
    }

    [Fact]
    public void A_run_pinned_at_a_common_cap_is_flagged_as_capped()
    {
        var random = new Random(1);
        var frames = Enumerable.Range(0, 20000).Select(_ => (1000.0 / 60) + ((random.NextDouble() - 0.5) * 0.2)).ToArray();
        var r = Analyze(TestData.Run(frames));

        var issue = Assert.Single(CaptureQuality.Assess(r.Info, r.Run, r.Metrics, r.Bottleneck), i => i.Id == "CAPPED");

        Assert.Contains("60", issue.Text);
        Assert.Contains("capture.quality_issues", r.Facts.Keys);
    }

    [Fact]
    public void An_idle_gpu_and_cpu_suggest_a_menu_and_missing_sensors_are_called_out()
    {
        var frames = TestData.Steady(20000, 10);
        var grid = TestData.SensorGrid(TestData.Times(frames)[^1] + 1);
        var idle = Analyze(TestData.Run(frames,
        [
            TestData.Sensor("a", "GPU Core", "Load", TestData.Const(grid, 8)),
            TestData.Sensor("b", "CPU Total", "Load", TestData.Const(grid, 6)),
            TestData.Sensor("c", "CPU Max", "Load", TestData.Const(grid, 12)),
        ], grid));
        var bare = Analyze(TestData.Run(frames));

        Assert.Contains(CaptureQuality.Assess(idle.Info, idle.Run, idle.Metrics, idle.Bottleneck), i => i.Id == "IDLE");
        Assert.Contains(CaptureQuality.Assess(bare.Info, bare.Run, bare.Metrics, bare.Bottleneck), i => i.Id == "NO_SENSORS");
    }

    private static ExperimentBaseline Before(double avg = 60, double low1 = 40, int freezes = 4, int stutters = 100) =>
        new() { AverageFps = avg, Low1Fps = low1, Low01Fps = 20, Freezes = freezes, Stutters = stutters };

    [Fact]
    public void An_experiment_that_removes_freezes_is_judged_as_helping_and_a_noisy_nothing_is_not()
    {
        var smooth = Analyze(TestData.Run(TestData.Steady(30000, 10)));   // no freezes, steady
        var helped = ExperimentJudge.Judge(Before(), smooth);
        Assert.Equal(ExperimentKind.Helped, helped.Kind);

        var same = ExperimentJudge.Judge(new ExperimentBaseline { AverageFps = smooth.Metrics.AverageFps, Low1Fps = smooth.Metrics.Low1AverageFps, Freezes = 0, Stutters = smooth.Stutters.Count }, smooth);
        Assert.Equal(ExperimentKind.NoClearChange, same.Kind);
        Assert.Contains("run-to-run variation", same.Text);
    }

    [Fact]
    public void An_experiment_that_adds_freezes_or_drops_the_lows_is_judged_worse()
    {
        var hitchy = Analyze(TestData.RunWithFreezes(95, 5));

        var worse = ExperimentJudge.Judge(Before(avg: 100, low1: 99, freezes: 0, stutters: 0), hitchy);

        Assert.Equal(ExperimentKind.Worse, worse.Kind);
    }

    [Fact]
    public void Experiments_are_stored_per_game_with_one_open_at_a_time_and_survive_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "daig-exp-" + Guid.NewGuid().ToString("N"), "experiments.json");
        try
        {
            var store = new ExperimentStore(path);
            store.Start(new Experiment { ProcessName = "game.exe", Game = "G", Step = "Turn DLSS on", Baseline = Before() });
            store.Start(new Experiment { ProcessName = "game.exe", Game = "G", Step = "Lower shadows", Baseline = Before() });
            store.Start(new Experiment { ProcessName = "other.exe", Game = "O", Step = "x", Baseline = Before() });

            var reopened = new ExperimentStore(path);
            Assert.Equal("Lower shadows", reopened.WaitingFor("game.exe")!.Step);
            Assert.Equal(2, reopened.All().Count(e => e.IsWaiting));

            reopened.Finish(reopened.WaitingFor("game.exe")!.Id, new ExperimentOutcome(ExperimentKind.Helped, "It helped."));
            Assert.Null(reopened.WaitingFor("game.exe"));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }

    [Fact]
    public void The_share_summary_has_the_numbers_and_the_main_findings_but_nothing_that_identifies_the_pc()
    {
        var file = TestData.File(TestData.RunWithFreezes(95, 90));
        file = new CaptureFile { Info = file.Info with { Motherboard = "ACME-BOARD-9000" }, Runs = file.Runs };
        var r = CaptureAnalyzer.Analyze(file);

        var text = ShareSummary.Build(r);

        Assert.Contains("Test Game", text);
        Assert.Contains("freeze(s)", text);
        Assert.Contains("Limited by", text);
        Assert.DoesNotContain("ACME-BOARD-9000", text);
        Assert.DoesNotContain(@"C:\Users", text);
        Assert.True(text.Length < 1800);
    }

    [Fact]
    public void The_built_in_sample_is_analysable_and_shows_real_findings()
    {
        var r = CaptureAnalyzer.Analyze(SampleCapture.Create());

        Assert.Equal(2, r.Freezes.Count);
        Assert.True(r.Metrics.AverageFps > 60);
        Assert.Equal("Sample Game", r.Info.Game);
        Assert.Equal("", r.Info.ProcessName);
    }
}
