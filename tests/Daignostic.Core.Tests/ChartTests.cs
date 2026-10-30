using Daignostic.Core.Analysis;

namespace Daignostic.Core.Tests;

public class ChartTests
{
    [Fact]
    public void Frame_time_and_fps_are_always_offered_other_views_only_when_data_exists()
    {
        var bare = TestData.Run(TestData.Steady(500, 10));
        Assert.Equal([ChartView.FrameTime, ChartView.Fps], ChartDataBuilder.Available(bare));

        var run = TestData.RunWithFreezes(95, 5);
        var views = ChartDataBuilder.Available(run);
        Assert.Contains(ChartView.Load, views);
        Assert.DoesNotContain(ChartView.Temperatures, views);
    }

    [Fact]
    public void Fps_view_is_the_inverse_of_frame_time_with_a_smoothed_overlay()
    {
        var spec = ChartDataBuilder.Build(TestData.Run(TestData.Steady(500, 10)), ChartView.Fps);
        Assert.Equal(100, spec.Lines[0].Values[0], 3);
        Assert.Equal("1 s average", spec.Lines[1].Name);
        Assert.Contains(spec.References, r => r.Value == 60);
    }

    [Fact]
    public void Load_view_is_fixed_to_a_hundred_percent_and_uses_sensor_time()
    {
        var run = TestData.RunWithFreezes(95, 5);
        var spec = ChartDataBuilder.Build(run, ChartView.Load);
        Assert.Equal(100, spec.FixedMax);
        Assert.Equal(["GPU load", "Busiest CPU thread"], spec.Lines.Select(l => l.Name));
        Assert.All(spec.Lines, l => Assert.Equal(run.SensorTimeSec.Length, l.Times.Length));
    }

    [Fact]
    public void Mismatched_sensor_lengths_are_skipped_rather_than_drawn_wrong()
    {
        var run = TestData.Run(TestData.Steady(100, 10),
            [TestData.Sensor("g", "GPU Core", "Load", [1, 2, 3])], TestData.SensorGrid(5));
        Assert.Empty(ChartDataBuilder.Build(run, ChartView.Load).Lines);
    }
}
