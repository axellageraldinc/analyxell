using Daignostic.Controls;
using Daignostic.Core.Analysis;
using Daignostic.Core.Mcp;
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
        ViewModel.CaptureChanged += OnCaptureChanged;
        Loaded += async (_, _) =>
        {
            OnCaptureChanged(ViewModel.Current);
            var pending = AppState.PendingOpenPaths;
            AppState.PendingOpenPaths = [];
            foreach (var path in pending)
            {
                await ViewModel.LoadPathAsync(path);
            }
        };
    }

    public AnalyzeViewModel ViewModel { get; }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // The AI endpoint may have changed in Settings since this cached page was last shown.
        ViewModel.Ai.RefreshPreview();
    }

    private void OnCaptureChanged(LoadedCapture? capture)
    {
        if (capture is null)
        {
            Chart.Clear();
            return;
        }

        var run = capture.Result.Run;
        var (avgTimes, avgValues) = Smoothing.BinAverage(run.TimeSec, run.FrameMs, 1.0);
        Chart.SetData(
            [
                new ChartSeries("Frame time", run.TimeSec, run.FrameMs, "AccentFillColorDefaultBrush", 1.2, 0.6),
                new ChartSeries("1 s average", avgTimes, avgValues, "TextFillColorPrimaryBrush", 2.0),
            ],
            capture.Result.Freezes.Select(f => f.TimeSec).ToList());
    }

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

