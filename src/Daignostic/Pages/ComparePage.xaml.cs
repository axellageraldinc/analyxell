using Daignostic.Core.Analysis;
using Daignostic.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Daignostic.Pages;

public sealed partial class ComparePage : Page
{
    public ComparePage()
    {
        ViewModel = new CompareViewModel();
        InitializeComponent();
        ViewModel.ComparisonChanged += OnComparisonChanged;
        Loaded += (_, _) => OnComparisonChanged(ViewModel.Comparison);
    }

    public CompareViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Ai.RefreshPreview();
    }

    private void OnComparisonChanged(ComparisonResult? comparison)
    {
        if (comparison is null || ViewModel.A is null || ViewModel.B is null)
        {
            Chart.Clear();
            return;
        }

        var a = ViewModel.A.Result.Run;
        var b = ViewModel.B.Result.Run;
        var spec = new ChartSpec(
            ChartView.FrameTime,
            "Frame time",
            "ms",
            "A in grey, B in blue. Lower is smoother.",
            [
                new ChartLine("A", a.TimeSec, a.FrameMs, 3),
                new ChartLine("B", b.TimeSec, b.FrameMs, 0),
            ],
            [new ChartReference(1000.0 / 60, "60 fps"), new ChartReference(1000.0 / 30, "30 fps")],
            null,
            ZeroBased: true,
            ClipSpikes: true);
        Chart.SetSpec(spec, ViewModel.B.Result.Freezes.Select(f => f.TimeSec).ToList());
    }
}
