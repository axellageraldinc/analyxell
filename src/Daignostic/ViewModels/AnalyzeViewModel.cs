using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daignostic.Core.Ai;
using Daignostic.Core.Analysis;
using Daignostic.Core.Background;
using Daignostic.Core.Capture;
using Daignostic.Core.Config;
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

public sealed record NextStepItem(string Number, string Text, string Why, bool CanTry = false);

public sealed record QualityView(string Text, string Fix);

public sealed record ChartViewItem(ChartView View, string Label);

/// <summary>One switch under the chart: show or hide one kind of marked moment.</summary>
public sealed partial class EventToggle(EventLayer layer, bool isOn, Action changed) : ObservableObject
{
    public EventLayer Layer { get; } = layer;

    public string Text => $"{Layer.Label} ({Layer.CountText})";

    [ObservableProperty]
    public partial bool IsOn { get; set; } = isOn;

    partial void OnIsOnChanged(bool value) => changed();
}

public sealed record BackgroundRow(string Name, string Cpu, string Disk, string Gpu, string Memory, string Note);

/// <summary>The sections of the Analyze page. Each one shows only what belongs to it, so the page stays readable.</summary>
public enum AnalyzeTab
{
    Overview,
    Ai,
    Findings,
    Pc,
    Background,
}

public sealed partial class AnalyzeViewModel : ObservableObject
{
    private LoadedCapture? _current;

    public AnalyzeViewModel()
    {
        Error = "";
        CatalogStatus = "";
        StatusMessage = "";
        ScanStatus = "";
        VerdictHeadline = "";
        VerdictDetail = "";
        CapFrameX = new CapFrameXViewModel();
        Ai = new AiPanelViewModel(RunAiAsync, BuildPreview, BuildPack, "daignostic-ai-pack", () => Current is null ? [] : AiChecks.Available(Current.Result));
        if (AppState.Selected is { } selected)
        {
            Show(selected);
        }
    }

    public ObservableCollection<MetricItem> Cards { get; } = [];

    public ObservableCollection<QualityView> QualityIssues { get; } = [];

    public bool HasQuality => QualityIssues.Count > 0;

    public string QualityText => string.Join(Environment.NewLine, QualityIssues.Select(q => "\u2022 " + q.Text + " " + q.Fix));

    // The change the user said they would try, and what came of it.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExperiment))]
    public partial string ExperimentTitle { get; set; } = "";

    [ObservableProperty]
    public partial string ExperimentText { get; set; } = "";

    [ObservableProperty]
    public partial InfoBarSeverity ExperimentSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool ExperimentCanCancel { get; set; }

    public bool HasExperiment => ExperimentTitle.Length > 0;

    private string _experimentId = "";

    // A capture of the same game that is newer than the one on screen.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNewerCapture))]
    public partial string NewerCaptureName { get; set; } = "";

    public bool HasNewerCapture => NewerCaptureName.Length > 0;

    private string _newerCapturePath = "";
    private string _dismissedNewerPath = "";

    [ObservableProperty]
    public partial string EmptyStatus { get; set; } = "";

    public ObservableCollection<FindingItem> AttentionFindings { get; } = [];

    public ObservableCollection<FindingItem> NoteFindings { get; } = [];

    public ObservableCollection<NextStepItem> TopSteps { get; } = [];

    public ObservableCollection<NextStepItem> MoreSteps { get; } = [];

    public ObservableCollection<SettingRow> SettingRows { get; } = [];

    public ObservableCollection<SettingRow> SystemRows { get; } = [];

    public ObservableCollection<SpecSectionView> SpecSections { get; } = [];

    public ObservableCollection<BackgroundRow> BackgroundRows { get; } = [];

    public ObservableCollection<ChartViewItem> ChartViews { get; } = [];

    public ObservableCollection<EventToggle> EventToggles { get; } = [];

    public ObservableCollection<CaptureListing> Catalog { get; } = [];

    public AiPanelViewModel Ai { get; }

    public CapFrameXViewModel CapFrameX { get; }

    /// <summary>Raised when the chart should be redrawn. The spec is null when nothing is loaded.</summary>
    public event Action<ChartSpec?, IReadOnlyList<EventLayer>>? ChartChanged;

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
                OnPropertyChanged(nameof(SettingsNote));
                OnPropertyChanged(nameof(HasSettingsNote));
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

    /// <summary>What the marks on the chart mean, for the kinds that are switched on.</summary>
    [ObservableProperty]
    public partial string EventExplanation { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewTab), nameof(IsAiTab), nameof(IsFindingsTab), nameof(IsPcTab), nameof(IsBackgroundTab))]
    public partial AnalyzeTab SelectedTab { get; set; }

    [ObservableProperty]
    public partial string VerdictHeadline { get; set; }

    [ObservableProperty]
    public partial string VerdictDetail { get; set; }

    [ObservableProperty]
    public partial InfoBarSeverity VerdictSeverity { get; set; }

    [ObservableProperty]
    public partial string FindingsTabText { get; set; } = "Findings";

    [ObservableProperty]
    public partial string MoreStepsHeader { get; set; } = "";

    [ObservableProperty]
    public partial string NotesHeader { get; set; } = "";

    /// <summary>What the last background-program check concluded, repeated here so the result is next to the button.</summary>
    [ObservableProperty]
    public partial string BackgroundSummary { get; set; } = "";

    public bool HasBackgroundSummary => BackgroundSummary.Length > 0;

    /// <summary>The other notes stay folded away unless there is nothing else to read.</summary>
    [ObservableProperty]
    public partial bool NotesExpanded { get; set; }

    public bool IsOverviewTab => SelectedTab == AnalyzeTab.Overview;

    public bool IsAiTab => SelectedTab == AnalyzeTab.Ai;

    public bool IsFindingsTab => SelectedTab == AnalyzeTab.Findings;

    public bool IsPcTab => SelectedTab == AnalyzeTab.Pc;

    public bool IsBackgroundTab => SelectedTab == AnalyzeTab.Background;

    public bool NotScanning => !IsScanning;

    public bool HasCapture => Current is not null;

    public bool IsEmpty => Current is null && !IsLoading;

    public bool HasError => Error.Length > 0;

    public bool HasSettings => Current?.Result.Settings is not null;

    /// <summary>Why the game's config file was not used for this capture, when it was saved after the capture was recorded.</summary>
    public string SettingsNote => Current?.SettingsNote ?? "";

    public bool HasSettingsNote => SettingsNote.Length > 0;

    public bool HasNoReader => Current is not null && Current.Result.Settings is null;

    public bool HasSystemRows => SystemRows.Count > 0;

    public bool HasTopSteps => TopSteps.Count > 0;

    public bool HasMoreSteps => MoreSteps.Count > 0;

    public bool HasAttention => AttentionFindings.Count > 0;

    public bool HasNotes => NoteFindings.Count > 0;

    public bool HasBackground => BackgroundRows.Count > 0;

    public bool HasEventToggles => EventToggles.Count > 0;

    public string SettingsHeader => Current?.Result.Settings is { } s ? $"Game settings: {s.GameName}" : "Game settings";

    public string Title => Current?.Result.Info.Game is { Length: > 0 } g ? g : Current?.Name ?? "No capture loaded";

    public string SpecSummary => Current?.Result.Specs.Summary ?? "";

    public string Subtitle => Current is null
        ? "Open a CapFrameX capture to analyze it."
        : $"{Current.Result.Metrics.DurationSec / 60:0.#} min capture  ·  {Current.Result.Info.GraphicsApi}";

    partial void OnSelectedChartViewChanged(ChartViewItem? value) => RaiseChart();

    [RelayCommand]
    private void ShowTab(AnalyzeTab tab) => SelectedTab = tab;

    /// <summary>Switches to the AI tab so the user can follow progress, then asks.</summary>
    [RelayCommand]
    private async Task AskAiAndShowAsync()
    {
        SelectedTab = AnalyzeTab.Ai;
        if (Ai.AskCommand.CanExecute(null))
        {
            await Ai.AskCommand.ExecuteAsync(null);
        }
    }

    /// <summary>The user says the game's current config is what they played with: keep it for this capture and re-analyze.</summary>
    [RelayCommand]
    private async Task UseLiveConfigAsync()
    {
        if (Current is { LiveConfig: { } live } capture)
        {
            AppState.SaveCaptureSettings(capture, live);
            await ReanalyzeAsync();
        }
    }

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

    /// <summary>Re-runs the analysis after the user changed settings or setup, keeping the chosen chart view and tab.</summary>
    public async Task ReanalyzeAsync(BackgroundScanResult? background = null)
    {
        if (Current is null)
        {
            return;
        }

        IsLoading = true;
        try
        {
            Show(await AppState.ReanalyzeAsync(Current, background), keepView: true);
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

    private void Show(LoadedCapture capture, bool keepView = false)
    {
        Current = capture;
        var r = capture.Result;
        var m = r.Metrics;

        var verdict = OverallVerdict.From(r);
        VerdictHeadline = verdict.Headline;
        VerdictDetail = verdict.Detail;
        VerdictSeverity = verdict.Level switch
        {
            Severity.High => InfoBarSeverity.Error,
            Severity.Medium => InfoBarSeverity.Warning,
            Severity.Low => InfoBarSeverity.Informational,
            _ => InfoBarSeverity.Success,
        };

        Cards.Clear();
        var fgOn = r.Facts.TryGetValue("framegen.rendered_fps", out var renderedText) && r.Facts.GetValueOrDefault("framegen.detected") == "yes";
        Cards.Add(new MetricItem("Average", F(m.AverageFps), fgOn ? "fps on screen, generated frames included" : "fps"));
        if (fgOn)
        {
            Cards.Add(new MetricItem("Rendered", renderedText!, "fps your PC actually draws"));
        }

        Cards.Add(new MetricItem("1% low", F(m.Low1AverageFps), "fps, slowest 1%"));
        Cards.Add(new MetricItem("0.1% low", F(m.Low01AverageFps), "fps, slowest 0.1%"));
        Cards.Add(new MetricItem("Stutters", r.Stutters.Count.ToString(CultureInfo.InvariantCulture), "spikes over 2x median"));
        Cards.Add(new MetricItem("Freezes", r.Freezes.Count.ToString(CultureInfo.InvariantCulture), "frames of 200 ms+"));
        Cards.Add(new MetricItem("Limited by", Verdict(r.Bottleneck.Verdict), $"{r.Bottleneck.Confidence} confidence"));

        AttentionFindings.Clear();
        NoteFindings.Clear();
        foreach (var f in r.Findings.OrderByDescending(f => f.Severity))
        {
            var item = new FindingItem(
                f.Title,
                f.Detail,
                "Based on: " + string.Join(", ", f.Evidence),
                Map(f),
                string.Join(Environment.NewLine, f.Suggestions.Select(s => "• " + s)),
                f.Suggestions.Count > 0);
            (f.Severity >= Severity.Medium ? AttentionFindings : NoteFindings).Add(item);
        }

        var attention = r.Findings.Count(f => f.Severity >= Severity.Low);
        FindingsTabText = attention > 0 ? $"Findings ({attention})" : "Findings";
        NotesHeader = NoteFindings.Count == 1 ? "1 other note" : $"{NoteFindings.Count} other notes";
        NotesExpanded = AttentionFindings.Count == 0;

        TopSteps.Clear();
        MoreSteps.Clear();
        foreach (var s in r.NextSteps)
        {
            (TopSteps.Count < 3 ? TopSteps : MoreSteps).Add(new NextStepItem(s.Order.ToString(CultureInfo.InvariantCulture), s.Text, s.FindingTitle, CanTry: capture.ProcessName.Length > 0));
        }

        MoreStepsHeader = MoreSteps.Count == 1 ? "Show 1 more step" : $"Show {MoreSteps.Count} more steps";

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

        BackgroundSummary = string.Join(
            Environment.NewLine + Environment.NewLine,
            r.Findings.Where(f => f.Id.StartsWith("BACKGROUND", StringComparison.Ordinal)).Select(f => f.Title + ". " + f.Detail));
        OnPropertyChanged(nameof(HasBackgroundSummary));

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

        var wasOn = EventToggles.Where(t => t.IsOn).Select(t => t.Layer.Kind).ToHashSet();
        var firstShow = EventToggles.Count == 0 || !keepView;
        EventToggles.Clear();
        foreach (var layer in ChartEventBuilder.Build(r.Run).Where(l => l.Count > 0))
        {
            // Freezes are on to begin with; after a re-analysis the user's choices are kept.
            EventToggles.Add(new EventToggle(layer, firstShow ? layer.Kind == EventKind.Freezes : wasOn.Contains(layer.Kind), RaiseChart));
        }

        OnPropertyChanged(nameof(HasEventToggles));

        var previous = SelectedChartView?.View;
        ChartViews.Clear();
        foreach (var view in ChartDataBuilder.Available(r.Run))
        {
            ChartViews.Add(new ChartViewItem(view, ChartDataBuilder.Build(r.Run, view).Label));
        }

        var keep = keepView && previous is not null ? ChartViews.FirstOrDefault(c => c.View == previous) : null;
        SelectedChartView = keep ?? ChartViews.FirstOrDefault();
        if (!keepView)
        {
            SelectedTab = AnalyzeTab.Overview;
        }

        OnPropertyChanged(nameof(HasSystemRows));
        OnPropertyChanged(nameof(HasTopSteps));
        OnPropertyChanged(nameof(HasMoreSteps));
        OnPropertyChanged(nameof(HasAttention));
        OnPropertyChanged(nameof(HasNotes));
        OnPropertyChanged(nameof(HasBackground));
        if (keepView)
        {
            // The user added information: keep the answer visible, but say it no longer covers everything.
            Ai.MarkStale();
        }
        else
        {
            Ai.Reset();
        }

        UpdateQuality(r);
        UpdateExperiment(capture);
        RaiseChart();
    }

    private void UpdateQuality(AnalysisResult r)
    {
        QualityIssues.Clear();
        foreach (var issue in CaptureQuality.Assess(r.Info, r.Run, r.Metrics, r.Bottleneck))
        {
            QualityIssues.Add(new QualityView(issue.Text, issue.Fix));
        }

        OnPropertyChanged(nameof(HasQuality));
        OnPropertyChanged(nameof(QualityText));
    }

    /// <summary>
    /// If the user said they would try a change for this game: remind them while the capture on screen is the "before"
    /// run, and judge the result when a later capture of the same game is opened.
    /// </summary>
    private void UpdateExperiment(LoadedCapture capture)
    {
        ExperimentTitle = "";
        ExperimentText = "";
        ExperimentCanCancel = false;
        _experimentId = "";
        var process = capture.ProcessName;
        if (process.Length == 0 || AppServices.Experiments.WaitingFor(process) is not { } exp)
        {
            return;
        }

        var key = CaptureSettingsResolver.KeyFor(capture.File.Info);
        _experimentId = exp.Id;
        if (key == exp.Baseline.CaptureKey)
        {
            ExperimentTitle = "You are trying: " + exp.Step;
            ExperimentText = "Make the change, then record the same route again. When you open the new capture here, daignostic will compare it with this one and tell you whether it helped.";
            ExperimentSeverity = InfoBarSeverity.Informational;
            ExperimentCanCancel = true;
            return;
        }

        var later = !DateTime.TryParse(exp.Baseline.RecordedAtUtc, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var before)
            || !DateTime.TryParse(capture.File.Info.RecordedAtUtc, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var now)
            || now > before;
        if (!later)
        {
            return;
        }

        var outcome = ExperimentJudge.Judge(exp.Baseline, capture.Result);
        AppServices.Experiments.Finish(exp.Id, outcome);
        ExperimentTitle = "You tried: " + exp.Step;
        ExperimentText = outcome.Text;
        ExperimentSeverity = outcome.Kind switch
        {
            ExperimentKind.Helped => InfoBarSeverity.Success,
            ExperimentKind.Worse => InfoBarSeverity.Warning,
            _ => InfoBarSeverity.Informational,
        };
    }

    /// <summary>The user says they will try this step: remember the current run as the "before".</summary>
    [RelayCommand]
    private void StartTry(string step)
    {
        if (Current is not { } capture || capture.ProcessName.Length == 0 || string.IsNullOrWhiteSpace(step))
        {
            return;
        }

        var key = CaptureSettingsResolver.KeyFor(capture.File.Info);
        AppServices.Experiments.Start(new Experiment
        {
            ProcessName = capture.ProcessName,
            Game = capture.Result.Info.Game,
            Step = step,
            Baseline = ExperimentBaseline.From(capture.Result, key, capture.Name),
        });
        UpdateExperiment(capture);
        StatusMessage = "Saved. Make the change, record the same route again, then open the new capture.";
    }

    [RelayCommand]
    private void CancelExperiment()
    {
        if (_experimentId.Length > 0)
        {
            AppServices.Experiments.Cancel(_experimentId);
        }

        ExperimentTitle = "";
        ExperimentText = "";
        ExperimentCanCancel = false;
    }

    [RelayCommand]
    private void DismissExperiment()
    {
        ExperimentTitle = "";
        ExperimentText = "";
    }

    /// <summary>Checks whether CapFrameX has recorded something newer than what is on screen. Quiet when nothing is configured.</summary>
    public async Task CheckForNewerAsync()
    {
        if (Current is null || IsLoading || (!AppServices.Settings.UseCapFrameXMcp && AppServices.Settings.CapturesFolder.Length == 0))
        {
            return;
        }

        try
        {
            var result = await CaptureCatalog.ListAsync(AppServices.Settings, AppServices.Http, CancellationToken.None);
            var currentTime = DateTime.TryParse(Current.File.Info.RecordedAtUtc, CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : DateTime.MinValue;
            var newest = result.Items.Where(i => i.RecordedAt is not null).OrderByDescending(i => i.RecordedAt).FirstOrDefault();
            if (newest is not null && !string.Equals(newest.Id, Current.Path, StringComparison.OrdinalIgnoreCase) &&
                newest.RecordedAt!.Value.UtcDateTime > currentTime.AddSeconds(5) && newest.Id != _dismissedNewerPath)
            {
                _newerCapturePath = newest.Id;
                NewerCaptureName = newest.FileName;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or JsonException)
        {
            // Looking for new captures is a convenience; failing quietly is right.
        }
    }

    [RelayCommand]
    private async Task OpenNewerAsync()
    {
        var path = _newerCapturePath;
        NewerCaptureName = "";
        if (path.Length > 0)
        {
            await LoadPathAsync(path);
        }
    }

    [RelayCommand]
    private void DismissNewer()
    {
        _dismissedNewerPath = _newerCapturePath;
        NewerCaptureName = "";
    }

    /// <summary>Opens the most recent capture CapFrameX or the captures folder knows about.</summary>
    [RelayCommand]
    private async Task OpenLatestAsync()
    {
        EmptyStatus = "Looking for your newest capture...";
        var result = await CaptureCatalog.ListAsync(AppServices.Settings, AppServices.Http, CancellationToken.None);
        var newest = result.Items.OrderByDescending(i => i.RecordedAt ?? DateTimeOffset.MinValue).FirstOrDefault();
        if (newest is null)
        {
            EmptyStatus = result.Warning ?? "No captures were found. Set where CapFrameX saves them in Settings, or choose a file with Open capture.";
            return;
        }

        EmptyStatus = "";
        await LoadPathAsync(newest.Id);
    }

    [RelayCommand]
    private async Task TrySampleAsync()
    {
        IsLoading = true;
        try
        {
            Show(await AppState.LoadSampleAsync());
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>Puts a short, anonymised summary on the clipboard, ready to paste into a forum or chat.</summary>
    [RelayCommand]
    private void CopySummary()
    {
        if (Current is null)
        {
            return;
        }

        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(ShareSummary.Build(Current.Result));
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        StatusMessage = "Summary copied. Paste it into a forum or chat to ask for help. It has no file paths, computer name or keys.";
    }

    public void RaiseChart()
    {
        if (Current is null || SelectedChartView is null)
        {
            ChartChanged?.Invoke(null, Array.Empty<EventLayer>());
            return;
        }

        var spec = ChartDataBuilder.Build(Current.Result.Run, SelectedChartView.View);
        var shown = EventToggles.Where(t => t.IsOn).Select(t => t.Layer).ToList();
        ChartTitle = $"{spec.Label} ({spec.Unit})";
        ChartCaption = spec.Caption;
        EventExplanation = shown.Count == 0
            ? "Switch on the markers below to see where problems happened."
            : string.Join("  ", shown.Select(l => $"{l.Label}: {l.Meaning}"));
        ChartChanged?.Invoke(spec, shown);
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
