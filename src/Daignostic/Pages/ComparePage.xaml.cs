using Daignostic.Controls;
using Daignostic.Core.Analysis;
using Daignostic.ViewModels;
using Microsoft.UI.Xaml;
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
        Chart.SetData(
            [
                new ChartSeries("A", a.TimeSec, a.FrameMs, "TextFillColorTertiaryBrush"),
                new ChartSeries("B", b.TimeSec, b.FrameMs, "AccentFillColorDefaultBrush"),
            ],
            ViewModel.B.Result.Freezes.Select(f => f.TimeSec).ToList());
    }
}
