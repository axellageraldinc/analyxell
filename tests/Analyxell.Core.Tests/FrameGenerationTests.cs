using Analyxell.Core.Analysis;
using Analyxell.Core.Capture;

namespace Analyxell.Core.Tests;

public class FrameGenerationTests
{
    private static double[] Alternating(int count) =>
        Enumerable.Range(0, count).Select(i => i % 2 == 0 ? 4.0 : 12.0).ToArray();

    [Fact]
    public void Alternating_frame_times_are_recognised_and_are_not_counted_as_stutter()
    {
        var run = TestData.Run(Alternating(6000));

        var fg = FrameGenerationSignal.Compute(run);
        var withoutDetection = Detectors.FindStutters(run.TimeSec, run.FrameMs);
        var result = CaptureAnalyzer.Analyze(TestData.File(run));

        Assert.True(fg.Active);
        Assert.Equal(2, fg.FramesPerRendered);
        Assert.True(withoutDetection.Count > 1000, "a plain test reads the alternation as stutter");
        Assert.Equal(0, result.Stutters.Count);
        Assert.Contains(result.Findings, f => f.Id == "FRAME_GENERATION_DETECTED");
    }

    [Fact]
    public void A_stream_of_real_and_generated_frames_is_judged_on_what_the_screen_shows()
    {
        // Each real frame (26 ms) is followed by a generated one 0.6 ms later; the screen changes evenly every 13.3 ms.
        var frames = Enumerable.Range(0, 8000).Select(i => i % 2 == 0 ? 26.0 : 0.6).ToArray();
        var run = new CaptureRun { TimeSec = TestData.Times(frames), FrameMs = frames, DisplayMs = TestData.Steady(8000, 13.3 * 0.9955) };
        var plain = FrameMetrics.Compute(run.FrameMs);

        var result = CaptureAnalyzer.Analyze(TestData.File(run));

        Assert.True(plain.MaxFps > 1000, "the generated frames read as 1,600 fps frames when taken at face value");
        Assert.True(result.Metrics.MaxFps < 100, "the displayed frames are what is judged");
        Assert.InRange(result.Metrics.AverageFps, 70, 80);
        var fg = FrameGenerationSignal.Compute(run);
        Assert.InRange(fg.RenderedFps, 35, 41);
        Assert.InRange(fg.DisplayedFps, 70, 80);
        Assert.Equal(0, result.Stutters.Count);
        var finding = Assert.Single(result.Findings, f => f.Id == "FRAME_GENERATION_DETECTED");
        Assert.Contains("Your PC draws about", finding.Detail);
        Assert.Equal(Severity.Low, finding.Severity);
        Assert.Contains(ChartDataBuilder.Build(run, ChartView.Fps).Lines, l => l.Times.Length == 8000 && l.Values.Max() < 100);
    }

    [Fact]
    public void A_real_freeze_is_still_a_freeze_under_frame_generation()
    {
        var frames = TestData.Concat(Alternating(3000), [400], Alternating(3000));

        var result = CaptureAnalyzer.Analyze(TestData.File(TestData.Run(frames)));

        Assert.Single(result.Freezes);
    }

    [Fact]
    public void Ordinary_runs_with_noise_are_not_flagged()
    {
        var random = new Random(5);
        var frames = Enumerable.Range(0, 6000).Select(_ => 10 + random.NextDouble() * 3).ToArray();

        Assert.False(FrameGenerationSignal.Compute(TestData.Run(frames)).Active);
        Assert.DoesNotContain(CaptureAnalyzer.Analyze(TestData.File(TestData.Run(frames))).Findings, f => f.Id == "FRAME_GENERATION_DETECTED");
    }

    [Fact]
    public void Frames_the_capture_labels_as_generated_are_recognised()
    {
        var frames = TestData.Steady(3000, 8);
        var run = new CaptureRun
        {
            TimeSec = TestData.Times(frames),
            FrameMs = frames,
            FrameTypes = frames.Select((_, i) => i % 2 == 0 ? "Application" : "AMD_AFMF").ToArray(),
        };

        var fg = FrameGenerationSignal.Compute(run);

        Assert.True(fg.Active);
        Assert.Equal(2, fg.FramesPerRendered);
        Assert.Equal(50, fg.GeneratedFramesPct, 0);
    }

    [Fact]
    public void A_display_showing_twice_the_presented_frames_is_flagged_without_merging_even_frames()
    {
        var frames = TestData.Steady(3000, 16);
        var run = new CaptureRun { TimeSec = TestData.Times(frames), FrameMs = frames, DisplayMs = TestData.Steady(3000, 8) };

        var fg = FrameGenerationSignal.Compute(run);

        Assert.True(fg.Active);
        Assert.Equal(1, fg.FramesPerRendered);
        var (times, ms) = fg.Merge(run.TimeSec, run.FrameMs);
        Assert.Equal(3000, ms.Length);
    }
}

public class ComparisonChangeTests
{
    private static AnalysisResult Run(params (string Key, string Value)[] settings)
    {
        var gs = settings.Length == 0
            ? null
            : new Analyxell.Core.Config.GameSettings("G", "x", DateTime.UtcNow, [.. settings.Select(s => new Analyxell.Core.Config.GameSetting(s.Key, s.Key, s.Value, false))]);
        return CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(3000, 10))), settings: gs);
    }

    [Fact]
    public void Settings_that_differ_between_runs_are_listed_and_flagged_when_they_change_the_frame_rate()
    {
        var cmp = RunComparer.Compare(Run(("dlss", "Off"), ("shadows", "High")), Run(("dlss", "On"), ("shadows", "High")));

        var change = Assert.Single(cmp.Changes);
        Assert.Equal("dlss", change.Key);
        Assert.Equal("Off", change.A);
        Assert.Equal("On", change.B);
        Assert.True(cmp.ChangesAffectFrameRate);
        Assert.Equal("A: Off, B: On", cmp.Facts["setting_changed.dlss"]);
    }

    [Fact]
    public void Unknown_settings_are_reported_as_unknown_not_as_unchanged()
    {
        var cmp = RunComparer.Compare(Run(), Run(("dlss", "On")));

        Assert.True(cmp.SettingsUnknown);
        Assert.Empty(cmp.Changes);
        Assert.Equal("no", cmp.Facts["settings_known_for_both_runs"]);
    }

    [Fact]
    public void Identical_settings_report_no_changes()
    {
        var cmp = RunComparer.Compare(Run(("dlss", "On")), Run(("dlss", "On")));

        Assert.Empty(cmp.Changes);
        Assert.False(cmp.SettingsUnknown);
    }
}
