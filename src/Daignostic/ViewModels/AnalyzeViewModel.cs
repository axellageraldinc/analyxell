using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daignostic.Core.Ai;
using Daignostic.Core.Analysis;
using Daignostic.Core.Background;
using Daignostic.Core.Mcp;
using Daignostic.Core.Reporting;
using Daignostic.Core.SystemInfo;
using Daignostic.Services;
using Microsoft.UI.Xaml.Controls;

namespace Daignostic.ViewModels;

public sealed record MetricItem(string Title, string Value, string Caption);

public sealed record FindingItem(string Title, string Detail, string Evidence, InfoBarSeverity Severity, string Suggestions, bool HasSuggestions);

public sealed record SettingRow(string Label, string Value, string Source);

public sealed record SpecSectionView(string Title, string Lines);

public sealed record NextStepItem(string Number, string Text, string Why);

public sealed record ChartViewItem(ChartView View, string Label);

public sealed record BackgroundRow(string Name, string Cpu, string Disk, string Gpu, string Memory, string Note);

public sealed partial class AnalyzeViewModel : ObservableObject
{
    private LoadedCapture? _current;

    public AnalyzeViewModel()
    {
        Error = "";
        CatalogStatus = "";
        StatusMessage = "";
        ScanStatus = "";
        Ai = new AiPanelViewModel(RunAiAsync, BuildPreview, BuildPack, "daignostic-ai-pack");
        if (AppState.Selected is { } selected)
        {
            Show(selected);
        }
    }

    public ObservableCollection<MetricItem> Cards { get; } = [];

    public ObservableCollection<FindingItem> Findings { get; } = [];

    public ObservableCollection<NextStepItem> NextSteps { get; } = [];

    public ObservableCollection<SettingRow> SettingRows { get; } = [];

    public ObservableCollection<SettingRow> SystemRows { get; } = [];

    public ObservableCollection<SpecSectionView> SpecSections { get; } = [];

    public ObservableCollection<BackgroundRow> BackgroundRows { get; } = [];

    public ObservableCollection<ChartViewItem> ChartViews { get; } = [];

    public ObservableCollection<CaptureListing> Catalog { get; } = [];

    public AiPanelViewModel Ai { get; }

    public CapFrameXViewModel CapFrameX { get; }

    /// <summary>Raised when the chart should be redrawn. The spec is null when nothing is loaded.</summary>
    public event Action<ChartSpec?, IReadOnlyList<double>>? ChartChanged;

    public LoadedCapture? Current
    {
        get => _current;
        private set
        {
            if (SetProperty(ref _current, value))
            {
                OnPropertyChanged(nameof(HasCapture));
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(Title));
                OnPropertyChanged(nameof(Subtitle));
                OnPropertyChanged(nameof(SpecSummary));
                OnPropertyChanged(nameof(HasSettings));
                OnPropertyChanged(nameof(SettingsHeader));
                OnPropertyChanged(nameof(HasNoReader));
                OnPropertyChanged(nameof(HasSystemRows));
                OnPropertyChanged(nameof(HasNextSteps));
                OnPropertyChanged(nameof(HasBackground));
            }
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty), nameof(HasError))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; }

    [ObservableProperty]
    public partial string CatalogStatus { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotScanning))]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string ScanStatus { get; set; }

    [ObservableProperty]
    public partial ChartViewItem? SelectedChartView { get; set; }

    [ObservableProperty]
    public partial string ChartTitle { get; set; } = "";

    [ObservableProperty]
    public partial string ChartCaption { get; set; } = "";

    public bool NotScanning => !IsScanning;

    public bool HasCapture => Current is not null;

    public bool IsEmpty => Current is null && !IsLoading;

    public bool HasError => Error.Length > 0;

    public bool HasSettings => Current?.Result.Settings is not null;

    public bool HasNoReader => Current is not null && Current.Result.Settings is null;

    public bool HasSystemRows => SystemRows.Count > 0;

    public bool HasNextSteps => NextSteps.Count > 0;

    public bool HasBackground => BackgroundRows.Count > 0;

    public string SettingsHeader => Current?.Result.Settings is { } s ? $"Game settings: {s.GameName}" : "Game settings";

    public string Title => Current?.Result.Info.Game is { Length: > 0 } g ? g : Current?.Name ?? "No capture loaded";

    public string SpecSummary => Current?.Result.Specs.Summary ?? "";

    public string Subtitle => Current is null
        ? "Open a CapFrameX capture to analyze it."
        : $"{Current.Result.Metrics.DurationSec / 60:0.#} min capture  ·  {Current.Result.Info.GraphicsApi}";

    partial void OnSelectedChartViewChanged(ChartViewItem? value) => RaiseChart();

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var path = await Pickers.PickCaptureAsync();
        if (path is not null)
        {
            await LoadPathAsync(path);
        }
    }

    public async Task LoadPathAsync(string path)
    {
        IsLoading = true;
        Error = "";
        try
        {
            Show(await AppState.LoadAsync(path));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            Error = ex is InvalidDataException or JsonException
                ? "That file is not a readable CapFrameX capture."
                : "The file could not be read: " + ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Re-runs the analysis after the user changed settings or setup, keeping the chosen chart view.</summary>
    public async Task ReanalyzeAsync(BackgroundScanResult? background = null)
    {
        if (Current is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            Show(await AppState.ReanalyzeAsync(Current, background), keepChartView: true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RefreshCatalogAsync()
    {
        CatalogStatus = "Looking for captures...";
        var result = await CaptureCatalog.ListAsync(AppServices.Settings, AppServices.Http, CancellationToken.None);
        Catalog.Clear();
        foreach (var item in result.Items)
        {
            Catalog.Add(item);
        }

        CatalogStatus = result.Items.Count == 0
            ? result.Warning ?? "No captures found."
            : $"{result.Items.Count} capture(s) from {result.Source}." + (result.Warning is null ? "" : " " + result.Warning);
    }

    [RelayCommand]
    private async Task CheckBackgroundAsync()
    {
        if (Current is null || IsScanning)
        {
            return;
        }

        IsScanning = true;
        ScanStatus = "Sampling running programs for 10 seconds...";
        try
        {
            var scan = await BackgroundScanner.RunAsync(new WindowsProcessSampler(), TimeSpan.FromSeconds(10), Current.ProcessName, CancellationToken.None);
            ScanStatus = "";
            await ReanalyzeAsync(scan);
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ScanStatus = "The check could not run: " + ex.Message;
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (Current is null)
        {
            return;
        }

        var path = await Pickers.PickReportPathAsync(Current.Name + "-report");
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, MarkdownReport.Build(Current.Result, Ai.Analysis));
            StatusMessage = "Report saved.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "The report could not be saved: " + ex.Message;
        }
    }

    private void Show(LoadedCapture capture, bool keepChartView = false)
    {
        Current = capture;
        var r = capture.Result;
        var m = r.Metrics;

        Cards.Clear();
        Cards.Add(new MetricItem("Average", F(m.AverageFps), "fps"));
        Cards.Add(new MetricItem("1% low", F(m.Low1AverageFps), "fps, slowest 1%"));
        Cards.Add(new MetricItem("0.1% low", F(m.Low01AverageFps), "fps, slowest 0.1%"));
        Cards.Add(new MetricItem("Stutters", r.Stutters.Count.ToString(CultureInfo.InvariantCulture), "spikes over 2x median"));
        Cards.Add(new MetricItem("Freezes", r.Freezes.Count.ToString(CultureInfo.InvariantCulture), "frames of 200 ms+"));
        Cards.Add(new MetricItem("Limited by", Verdict(r.Bottleneck.Verdict), $"{r.Bottleneck.Confidence} confidence"));

        Findings.Clear();
        foreach (var f in r.Findings.OrderByDescending(f => f.Severity))
        {
            Findings.Add(new FindingItem(
                f.Title,
                f.Detail,
                "Based on: " + string.Join(", ", f.Evidence),
                Map(f),
                string.Join(Environment.NewLine, f.Suggestions.Select(s => "• " + s)),
                f.Suggestions.Count > 0));
        }

        NextSteps.Clear();
        foreach (var s in r.NextSteps)
        {
            NextSteps.Add(new NextStepItem(s.Order.ToString(CultureInfo.InvariantCulture), s.Text, s.FindingTitle));
        }

        SettingRows.Clear();
        if (r.Settings is { } settings)
        {
            foreach (var item in settings.Items)
            {
                SettingRows.Add(new SettingRow(item.Label, item.Value, item.Source));
            }
        }

        SystemRows.Clear();
        foreach (var item in r.System.Items.Where(i => i.Key is not ("game_install_path_known" or "display_current_hz")))
        {
            SystemRows.Add(new SettingRow(item.Label, item.Value, item.Source == SettingSource.User ? "entered by you" : "read from Windows"));
        }

        SpecSections.Clear();
        foreach (var section in r.Specs.Sections)
        {
            SpecSections.Add(new SpecSectionView(section.Title, string.Join(Environment.NewLine, section.Items.Select(i => $"{i.Label}: {i.Value}"))));
        }

        BackgroundRows.Clear();
        if (r.Background is { } scan)
        {
            foreach (var p in scan.Processes)
            {
                BackgroundRows.Add(new BackgroundRow(
                    p.IsGame ? p.Name + " (the game)" : p.Name,
                    F(p.CpuPct) + "%",
                    F(p.DiskMBps) + " MB/s",
                    p.GpuPct is { } g ? F(g) + "%" : "n/a",
                    F(p.MemoryMb) + " MB",
                    p.Category));
            }
        }

        var previous = SelectedChartView?.View;
        ChartViews.Clear();
        foreach (var view in ChartDataBuilder.Available(r.Run))
        {
            ChartViews.Add(new ChartViewItem(view, ChartDataBuilder.Build(r.Run, view).Label));
        }

        var keep = keepChartView && previous is not null ? ChartViews.FirstOrDefault(c => c.View == previous) : null;
        SelectedChartView = keep ?? ChartViews.FirstOrDefault();

        OnPropertyChanged(nameof(HasSystemRows));
        OnPropertyChanged(nameof(HasNextSteps));
        OnPropertyChanged(nameof(HasBackground));
        Ai.Reset();
        RaiseChart();
    }

    public void RaiseChart()
    {
        if (Current is null || SelectedChartView is null)
        {
            ChartChanged?.Invoke(null, []);
            return;
        }

        var spec = ChartDataBuilder.Build(Current.Result.Run, SelectedChartView.View);
        ChartTitle = $"{spec.Label} ({spec.Unit})";
        ChartCaption = spec.Caption;
        ChartChanged?.Invoke(spec, Current.Result.Freezes.Select(f => f.TimeSec).ToList());
    }

    private string? BuildPreview(string? note) =>
        Current is null ? null : AiPrompts.BuildSingle(Current.Result, note);

    private string? BuildPack(string? note) =>
        Current is null ? null : AiExportPack.BuildSingle(Current.Result, note, DateTime.UtcNow);

    private Task<AiAnalysis> RunAiAsync(string? note, CancellationToken ct) =>
        Current is null
            ? throw new AiException("Load a capture first.")
            : AppServices.CreateAnalyst().AnalyzeAsync(Current.Result, note, ct);

    private static InfoBarSeverity Map(Finding f) => f.Id == "SMOOTH" || f.Id == "BACKGROUND_QUIET" ? InfoBarSeverity.Success : f.Severity switch
    {
        Severity.High => InfoBarSeverity.Error,
        Severity.Medium => InfoBarSeverity.Warning,
        _ => InfoBarSeverity.Informational,
    };

    internal static string Verdict(BottleneckVerdict v) => v switch
    {
        BottleneckVerdict.GpuBound => "GPU",
        BottleneckVerdict.CpuBound => "CPU",
        BottleneckVerdict.Balanced => "Balanced",
        BottleneckVerdict.ThermalThrottling => "Heat",
        BottleneckVerdict.PowerLimited => "Power",
        _ => "Unknown",
    };

    private static string F(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
}
