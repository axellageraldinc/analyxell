using Analyxell.Core.Ai;
using Analyxell.Core.Analysis;
using Analyxell.Core.Capture;
using Analyxell.Core.SystemInfo;

namespace Analyxell.Core.Tests;

public class DeepRuleTests
{
    private static AnalysisResult Analyze(CaptureRun run, string ram = "16GB (2x8GB) 3600MT/s", SystemContext? system = null)
    {
        var file = TestData.File(run);
        file = new CaptureFile { Info = file.Info with { SystemRam = ram }, Runs = file.Runs };
        return CaptureAnalyzer.Analyze(file, system: system);
    }

    private static CaptureRun WithSensors(double[] frames, Func<double[], IEnumerable<SensorSeries>> sensors)
    {
        var grid = TestData.SensorGrid(TestData.Times(frames)[^1] + 1);
        return TestData.Run(frames, sensors(grid), grid);
    }

    [Fact]
    public void One_maxed_thread_with_an_idle_rest_is_reported_as_a_single_thread_limit()
    {
        var run = WithSensors(TestData.Steady(3000, 20), g =>
        [
            TestData.Sensor("a", "GPU Core", "Load", TestData.Const(g, 60)),
            TestData.Sensor("b", "CPU Max", "Load", TestData.Const(g, 96)),
            TestData.Sensor("c", "CPU Total", "Load", TestData.Const(g, 30)),
        ]);

        var r = Analyze(run);

        Assert.Contains(r.Findings, f => f.Id == "CPU_BOUND" && f.Title.Contains("holding the GPU back"));
        Assert.Contains(r.Findings, f => f.Id == "CPU_SINGLE_THREAD");
    }

    [Fact]
    public void A_gpu_bound_run_still_reports_the_stretches_where_the_cpu_limited_it()
    {
        var run = WithSensors(TestData.Steady(12000, 10), g =>
        {
            var limited = g.Select((_, i) => i >= g.Length * 0.8).ToArray();
            return
            [
                TestData.Sensor("a", "GPU Core", "Load", g.Select((_, i) => limited[i] ? 60.0 : 97.0).ToArray()),
                TestData.Sensor("b", "CPU Max", "Load", g.Select((_, i) => limited[i] ? 95.0 : 50.0).ToArray()),
            ];
        });

        var r = Analyze(run);

        Assert.Equal(BottleneckVerdict.GpuBound, r.Bottleneck.Verdict);
        Assert.Contains(r.Findings, f => f.Id == "CPU_LIMITED_MOMENTS");
    }

    [Fact]
    public void A_frame_rate_that_falls_during_the_run_is_flagged_with_the_memory_growth_that_fits()
    {
        var frames = TestData.Concat(TestData.Steady(6000, 10), TestData.Steady(10000, 12), TestData.Steady(3750, 16));
        var run = WithSensors(frames, g => [TestData.Sensor("v", "GPU Memory Dedicated", "Data", g.Select((_, i) => 6.0 + 4.0 * i / g.Length).ToArray())]);

        var finding = Assert.Single(Analyze(run).Findings, f => f.Id == "PERFORMANCE_DECLINES");

        Assert.Contains("video memory use grew", finding.Detail);
    }

    private static CaptureRun VramRun(double usedFraction, double fullShare)
    {
        // Three minutes at 100 fps; video memory (8 GB card) is at usedFraction for the last fullShare of the run.
        return WithSensors(TestData.Steady(18000, 10), g =>
        [
            TestData.Sensor("v", "GPU Memory Dedicated", "Data", g.Select((_, i) => i >= g.Length * (1 - fullShare) ? 8.0 * usedFraction : 4.0).ToArray()),
            TestData.Sensor("t", "GPU Memory Total", "Data", TestData.Const(g, 8)),
        ]);
    }

    [Fact]
    public void Video_memory_that_stays_nearly_full_is_called_a_likely_cause_of_texture_pop_in()
    {
        var r = Analyze(VramRun(0.97, 0.6));

        var finding = Assert.Single(r.Findings, f => f.Id == "TEXTURE_POP_IN_LIKELY");
        Assert.Equal(Severity.Low, finding.Severity);
        Assert.Contains("likely explanation, not a proven one", finding.Detail);
        Assert.Contains("vram.near_full_pct", finding.Evidence);
        Assert.InRange(double.Parse(r.Facts["vram.near_full_pct"], System.Globalization.CultureInfo.InvariantCulture), 55, 65);
        Assert.Contains(r.NextSteps, s => s.FindingId == "VRAM_PRESSURE" || s.FindingId == "TEXTURE_POP_IN_LIKELY");
    }

    [Fact]
    public void A_short_spike_or_spare_video_memory_does_not_report_pop_in()
    {
        Assert.DoesNotContain(Analyze(VramRun(0.97, 0.05)).Findings, f => f.Id == "TEXTURE_POP_IN_LIKELY");
        Assert.DoesNotContain(Analyze(VramRun(0.80, 0.9)).Findings, f => f.Id == "TEXTURE_POP_IN_LIKELY");
        Assert.DoesNotContain(Analyze(TestData.Run(TestData.Steady(18000, 10))).Findings, f => f.Id == "TEXTURE_POP_IN_LIKELY");
    }

    [Fact]
    public void Pop_in_on_a_hard_disk_also_suggests_an_ssd()
    {
        var ctx = new SystemContext([new SystemSetting("game_drive.media", "Drive type", "HDD", SettingSource.Auto)]);

        var finding = Assert.Single(Analyze(VramRun(0.97, 0.6), system: ctx).Findings, f => f.Id == "TEXTURE_POP_IN_LIKELY");

        Assert.Contains("hard disk", finding.Detail);
        Assert.Contains(finding.Suggestions, s => s.Contains("SSD"));
        Assert.Contains("system.game_drive.media", finding.Evidence);
    }

    [Fact]
    public void The_AI_may_cite_the_pop_in_finding_and_is_told_to_keep_it_a_hypothesis()
    {
        var r = Analyze(VramRun(0.97, 0.6));

        Assert.Contains("finding:TEXTURE_POP_IN_LIKELY", AiPrompts.ValidKeys(r));
        Assert.Contains("TEXTURE_POP_IN_LIKELY", AiPrompts.BuildSingle(r, null));
        Assert.Contains("TEXTURE_POP_IN_LIKELY", AiPrompts.System);
    }

    [Fact]
    public void A_steady_run_does_not_decline()
    {
        Assert.DoesNotContain(Analyze(TestData.Run(TestData.Steady(30000, 8))).Findings, f => f.Id == "PERFORMANCE_DECLINES");
    }

    [Fact]
    public void Hitches_that_cluster_early_are_called_a_likely_cache_effect()
    {
        // Nine freezes in the first minute, none afterwards.
        var parts = new List<double[]>();
        for (var i = 0; i < 9; i++)
        {
            parts.Add(TestData.Steady(400, 10));
            parts.Add([300]);
        }

        parts.Add(TestData.Steady(20000, 10));
        var r = Analyze(TestData.Run(TestData.Concat([.. parts])));

        var finding = Assert.Single(r.Findings, f => f.Id == "HITCHES_FADE");
        Assert.Contains("likely explanation, not a proven one", finding.Detail);
    }

    [Theory]
    [InlineData("16GB (1x16GB) 2400MT/s", true, true)]
    [InlineData("32GB (2x16GB) 3600MT/s", false, false)]
    public void Memory_set_up_badly_is_called_out_when_the_game_hitches(string ram, bool single, bool slow)
    {
        var frames = TestData.Concat(TestData.Steady(2000, 10), [300], TestData.Steady(2000, 10), [300], TestData.Steady(2000, 10));
        var r = Analyze(TestData.Run(frames), ram);

        Assert.Equal(single, r.Findings.Any(f => f.Id == "RAM_SINGLE_MODULE"));
        Assert.Equal(slow, r.Findings.Any(f => f.Id == "RAM_SLOW_SPEED"));
    }

    [Fact]
    public void Memory_text_is_parsed()
    {
        Assert.Equal(new MemoryConfig(2, 3200), MemoryConfig.Parse("32GB (2x16GB) 3200MT/s"));
        Assert.Equal(new MemoryConfig(null, null), MemoryConfig.Parse("32GB"));
    }

    [Fact]
    public void The_AI_is_offered_only_checks_that_would_add_something_and_may_only_name_those()
    {
        var plain = Analyze(TestData.Run(TestData.Steady(3000, 10)));
        var offered = AiChecks.Available(plain).Select(c => c.Id).ToList();
        Assert.Contains(AiChecks.Background, offered);
        Assert.Contains(AiChecks.PcSetup, offered);
        Assert.Contains("available_checks", AiPrompts.BuildSingle(plain, null));

        var reply = """{"summary":"x","inconclusive":false,"findings":[{"title":"t","explanation":"e","evidence":["fps.average"],"actions":[],"confidence":"low"}],"unknowns":[],"next_checks":["background_check","delete_everything","background_check"]}""";
        var parsed = AiResponseParser.Parse(reply, plain.Facts.Keys.ToHashSet(), offered.ToHashSet());

        Assert.Equal([AiChecks.Background], parsed.NextChecks);
    }

    [Fact]
    public void A_check_that_was_already_done_is_not_offered_again()
    {
        var ctx = new SystemContext(
        [
            new SystemSetting("setup.vsync", "VSync", "Off", SettingSource.User),
            new SystemSetting("setup.display_sync", "Display sync", "G-SYNC", SettingSource.User),
            new SystemSetting("setup.frame_cap", "Frame cap", "141", SettingSource.User),
        ]);

        var offered = AiChecks.Available(Analyze(TestData.Run(TestData.Steady(3000, 10)), system: ctx)).Select(c => c.Id);

        Assert.DoesNotContain(AiChecks.PcSetup, offered);
    }

    [Fact]
    public void VSync_set_in_the_driver_is_respected_because_a_capture_cannot_see_it()
    {
        var frames = TestData.Steady(6000, 8);
        var run = new CaptureRun
        {
            TimeSec = TestData.Times(frames),
            FrameMs = frames,
            AllowsTearing = frames.Select(_ => 1.0).ToArray(),
            SyncInterval = frames.Select(_ => 0.0).ToArray(),
        };
        var system = new SystemContext([new SystemSetting("setup.vsync", "VSync", "On (driver)", SettingSource.User)]);

        var withSetup = Analyze(run, system: system);
        var without = Analyze(run);

        Assert.Contains(withSetup.Findings, f => f.Id == "TEARING_HANDLED_BY_DRIVER");
        Assert.DoesNotContain(withSetup.Findings, f => f.Id.StartsWith("TEARING_P") || f.Id == "TEARING_UNLIKELY");
        Assert.DoesNotContain(without.Findings, f => f.Id == "TEARING_HANDLED_BY_DRIVER");
    }
}
