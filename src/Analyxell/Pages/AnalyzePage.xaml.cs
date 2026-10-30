using Analyxell.Core.Ai;
using Analyxell.Core.Analysis;
using Analyxell.Core.Mcp;
using Analyxell.Dialogs;
using Analyxell.Services;
using Analyxell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Analyxell.Pages;

public sealed partial class AnalyzePage : Page
{
    public AnalyzePage()
    {
        ViewModel = new AnalyzeViewModel();
        InitializeComponent();
        ViewModel.ChartChanged += OnChartChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Ai.CheckRequested += OnCheckRequested;

        // Quietly notice a newer capture of the game while this page is open.
        var watcher = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        watcher.Tick += async (_, _) => await ViewModel.CheckForNewerAsync();
        Loaded += (_, _) => watcher.Start();
        Unloaded += (_, _) => watcher.Stop();
        TabFindings.Text = ViewModel.FindingsTabText;
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

    private void OnChartChanged(ChartSpec? spec, IReadOnlyList<EventLayer> layers) => Chart.SetSpec(spec, layers);

    private bool _syncingTabs;

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AnalyzeViewModel.FindingsTabText):
                TabFindings.Text = ViewModel.FindingsTabText;
                break;
            case nameof(AnalyzeViewModel.SelectedTab):
                // Keeps the tab strip in step when the code switches tabs (for example Ask AI from the overview).
                _syncingTabs = true;
                Tabs.SelectedItem = ViewModel.SelectedTab switch
                {
                    AnalyzeTab.Ai => TabAi,
                    AnalyzeTab.Findings => TabFindings,
                    AnalyzeTab.Pc => TabPc,
                    AnalyzeTab.Background => TabBackground,
                    _ => TabOverview,
                };
                _syncingTabs = false;
                break;
        }
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_syncingTabs && sender.SelectedItem?.Tag is string tag && Enum.TryParse<AnalyzeTab>(tag, out var tab))
        {
            ViewModel.SelectedTab = tab;
        }
    }

    private void TryStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string step })
        {
            ViewModel.StartTryCommand.Execute(step);
        }
    }

    private void OpenAiTab_Click(object sender, RoutedEventArgs e) => ViewModel.SelectedTab = AnalyzeTab.Ai;

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

    /// <summary>Runs a check the AI asked for. Each one is a read-only check or a form the user fills in; the AI runs nothing.</summary>
    private async void OnCheckRequested(string id)
    {
        switch (id)
        {
            case AiChecks.Background:
                ViewModel.Ai.CheckStatus = "Checking running programs for 10 seconds...";
                await ViewModel.CheckBackgroundCommand.ExecuteAsync(null);
                ViewModel.Ai.CheckStatus = "Done. Press Ask AI again to include the result.";
                break;
            case AiChecks.PcSetup:
                Setup_Click(this, new RoutedEventArgs());
                break;
            case AiChecks.GameSettings:
                GameSettings_Click(this, new RoutedEventArgs());
                break;
        }
    }

    private async void GameSettings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Current is { } capture && await GameSettingsDialog.ShowAsync(XamlRoot, capture))
        {
            await ViewModel.ReanalyzeAsync();
        }
    }

    private async void GameSetup_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Current is not { } capture)
        {
            return;
        }

        var changed = await GameSetupDialog.RunAsync(
            XamlRoot,
            capture.Result.Info.Game,
            capture.ProcessName,
            "",
            _ => GameSettingsDialog.ShowAsync(XamlRoot, capture));
        if (changed)
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
