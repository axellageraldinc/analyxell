using Daignostic.Core.Analysis;

namespace Daignostic.Core.Tests;

public class VerdictAndCompareChartTests
{
    [Fact]
    public void A_smooth_run_gets_a_healthy_verdict_and_a_run_with_freezes_does_not()
    {
        var good = OverallVerdict.From(CaptureAnalyzer.Analyze(TestData.File(TestData.Run(TestData.Steady(3000, 10)))));
        Assert.Equal(Severity.Info, good.Level);
        Assert.Contains("healthy", good.Headline);
        Assert.Contains("No freezes", good.Detail);

        var bad = OverallVerdict.From(CaptureAnalyzer.Analyze(TestData.File(TestData.RunWithFreezes(95, 10))));
        Assert.True(bad.Level >= Severity.Medium || bad.Level >= Severity.Low);
        Assert.Contains("2 freezes", bad.Detail);
        Assert.Contains("step 1", bad.Detail);
    }

    [Fact]
    public void Comparison_charts_draw_both_runs_in_different_colours_with_named_lines()
    {
        var a = TestData.RunWithFreezes(95, 5);
        var b = TestData.Run(TestData.Steady(6000, 9));
        var spec = ChartDataBuilder.BuildComparison(a, b, ChartView.FrameTime, "before", "after");

        Assert.Contains(spec.Lines, l => l.Name == "A: before" && l.Palette == 1);
        Assert.Contains(spec.Lines, l => l.Name == "B: after" && l.Palette == 0);
        Assert.NotEqual(spec.Lines.First(l => l.Name == "A: before").Palette, spec.Lines.First(l => l.Name == "B: after").Palette);
        Assert.All(spec.Lines.Where(l => l.Name.Contains("all frames")), l => Assert.True(l.Opacity < 0.5));
    }

    [Fact]
    public void Sensor_views_compare_one_metric_and_only_when_both_runs_have_it()
    {
        var withLoad = TestData.RunWithFreezes(95, 5);
        var without = TestData.Run(TestData.Steady(3000, 10));

        var both = ChartDataBuilder.BuildComparison(withLoad, TestData.RunWithFreezes(60, 5), ChartView.Load, "a", "b");
        Assert.Equal(2, both.Lines.Count);
        Assert.Equal(100, both.FixedMax);

        Assert.Empty(ChartDataBuilder.BuildComparison(withLoad, without, ChartView.Load, "a", "b").Lines);
        var available = ChartDataBuilder.AvailableForComparison(withLoad, without);
        Assert.Contains(ChartView.FrameTime, available);
        Assert.DoesNotContain(ChartView.Load, available);
    }
}
