using Daignostic.Core.Analysis;
using Daignostic.Core.Mcp;
using Daignostic.Dialogs;
using Daignostic.Services;
using Daignostic.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Daignostic.Pages;

public sealed partial class AnalyzePage : Page
{
    public AnalyzePage()
    {
        ViewModel = new AnalyzeViewModel();
        InitializeComponent();
        ViewModel.ChartChanged += OnChartChanged;
        Loaded += async (_, _) =>
        {
            ViewModel.RaiseChart();
            var pending = AppState.PendingOpenPaths;
            AppState.PendingOpenPaths = [];
            foreach (var path in pending)
            {
                await ViewModel.LoadPathAsync(path);
            }

            if (ViewModel.Current is not null && pending.Count == 0)
            {
                ViewModel.Ai.RefreshPreview();
            }
        };
    }

    public AnalyzeViewModel ViewModel { get; }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // The AI settings may have changed since this cached page was last shown.
        ViewModel.Ai.RefreshPreview();
    }

    private void OnChartChanged(ChartSpec? spec, IReadOnlyList<double> freezes) => Chart.SetSpec(spec, freezes);

    private async void RecentButton_Click(object sender, RoutedEventArgs e) =>
        await ViewModel.RefreshCatalogCommand.ExecuteAsync(null);

    private async void Catalog_ItemClick(object sender, ItemClickEventArgs e)
    {
        RecentButton.Flyout?.Hide();
        if (e.ClickedItem is CaptureListing item)
        {
            await ViewModel.LoadPathAsync(item.Id);
        }
    }

    private async void GameSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Current is { } capture && await GameSettingsDialog.ShowAsync(XamlRoot, capture))
        {
            await ViewModel.ReanalyzeAsync();
        }
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Current is { } capture && await SetupDialog.ShowAsync(XamlRoot, capture))
        {
            await ViewModel.ReanalyzeAsync();
        }
    }

    private void Page_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Open capture";
    }

    private async void Page_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        if (items.FirstOrDefault(i => i is StorageFile f && f.FileType.Equals(".json", StringComparison.OrdinalIgnoreCase)) is StorageFile file)
        {
            await ViewModel.LoadPathAsync(file.Path);
        }
    }
}
