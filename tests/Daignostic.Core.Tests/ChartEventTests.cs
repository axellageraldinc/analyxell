using Daignostic.Core.Analysis;

namespace Daignostic.Core.Tests;

public class ChartEventTests
{
    [Fact]
    public void Freezes_and_stutters_are_marked_at_their_frame_times()
    {
        var run = TestData.RunWithFreezes(95, 5);
        var layers = ChartEventBuilder.Build(run);

        Assert.Equal(2, layers.First(l => l.Kind == EventKind.Freezes).Count);
        Assert.All(layers.First(l => l.Kind == EventKind.Freezes).Events, e => Assert.Equal(e.StartSec, e.EndSec));
    }

    [Fact]
    public void A_steady_run_has_nothing_to_mark()
    {
        var layers = ChartEventBuilder.Build(TestData.Run(TestData.Steady(6000, 10)));

        Assert.All(layers, l => Assert.Equal(0, l.Count));
        Assert.DoesNotContain(layers, l => l.Kind is EventKind.DroppedFrames or EventKind.TearingPossible);
    }

    [Fact]
    public void A_slow_stretch_is_one_fps_drop_span()
    {
        // 20 s at 100 fps, 5 s at 40 fps, 20 s at 100 fps.
        var frames = TestData.Steady(2000, 10).Concat(TestData.Steady(200, 25)).Concat(TestData.Steady(2000, 10)).ToArray();
        var drops = ChartEventBuilder.Build(TestData.Run(frames)).First(l => l.Kind == EventKind.FpsDrops);

        var span = Assert.Single(drops.Events);
        Assert.InRange(span.StartSec, 19, 21);
        Assert.InRange(span.EndSec - span.StartSec, 4, 7);
        Assert.True(drops.IsSpan);
    }
}
