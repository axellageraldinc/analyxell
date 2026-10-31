using Analyxell.Services;
using Analyxell.Core.Analysis;
using Analyxell.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Analyxell.Pages;

public sealed partial class ComparePage : Page
{
    public ComparePage()
    {
        ViewModel = new CompareViewModel();
        InitializeComponent();
        PageLayout.Constrain(Scroller, PageContent, 1240);
        ViewModel.ChartChanged += OnChartChanged;
        Loaded += (_, _) => ViewModel.RaiseChart();
    }

    public CompareViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // The page is cached, so re-bind the lists to pick up captures loaded since it was last shown.
        ViewModel.SyncWithLoaded();
        foreach (var combo in new[] { BaselineCombo, LaterCombo })
        {
            var selected = combo.SelectedItem;
            combo.ItemsSource = null;
            combo.ItemsSource = ViewModel.Loaded;
            combo.SelectedItem = selected;
        }

        ViewModel.Ai.RefreshPreview();
    }

    private void OnChartChanged(ChartSpec? spec, IReadOnlyList<double> freezes)
    {
        if (spec is null)
        {
            Chart.Clear();
            return;
        }

        Chart.SetSpec(spec, freezes);
    }
}
