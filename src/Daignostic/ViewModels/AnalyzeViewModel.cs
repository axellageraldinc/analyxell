using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using Daignostic.Core.Ai;
using Daignostic.Core.Analysis;
using Daignostic.Core.Mcp;
using Daignostic.Core.Reporting;
using Daignostic.Services;

namespace Daignostic.ViewModels;

public sealed record MetricItem(string Title, string Value, string Caption);

public sealed record FindingItem(string Title, string Detail, string Evidence, InfoBarSeverity Severity);

public sealed record SettingRow(string Label, string Value);

public sealed partial class AnalyzeViewModel : ObservableObject
{
    public AnalyzeViewModel()
    {
        Error = "";
        CatalogStatus = "";
        StatusMessage = "";
        Ai = new AiPanelViewModel(RunAiAsync, BuildPreview);
        if (AppState.Selected is { } selected)
        {
            Show(selected);
        }
    }

    public ObservableCollection<MetricItem> Cards { get; } = [];

    public ObservableCollection<FindingItem> Findings { get; } = [];

    public ObservableCollection<SettingRow> SettingRows { get; } = [];

    public ObservableCollection<CaptureListing> Catalog { get; } = [];

    public AiPanelViewModel Ai { get; }

    /// <summary>Raised when the chart should be redrawn for a new capture (or cleared with null).</summary>
    public event Action<LoadedCapture?>? CaptureChanged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCapture), nameof(IsEmpty), nameof(Title), nameof(Subtitle), nameof(HasSettings), nameof(SettingsHeader))]
    public partial LoadedCapture? Current { get; set; }

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

    public bool HasCapture => Current is not null;

    public bool IsEmpty => Current is null && !IsLoading;

    public bool HasError => Error.Length > 0;

    public bool HasSettings => Current?.Result.Settings is not null;

    public string SettingsHeader => Current?.Result.Settings is { } s ? $"Game settings: {s.GameName}" : "Game settings";

    public string Title => Current?.Result.Info.Game is { Length: > 0 } g ? g : Current?.Name ?? "No capture loaded";

    public string Subtitle
    {
        get
        {
            if (Current is null)
            {
                return "Open a CapFrameX capture to analyze it.";
            }

            var i = Current.Result.Info;
            var parts = new[] { i.Gpu, i.Cpu, i.Resolution, i.GraphicsApi, $"{Current.Result.Metrics.DurationSec / 60:0.#} min" };
            return string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
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

    private void Show(LoadedCapture capture)
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
            Findings.Add(new FindingItem(f.Title, f.Detail, "Based on: " + string.Join(", ", f.Evidence), Map(f)));
        }

        SettingRows.Clear();
        if (r.Settings is { } settings)
        {
            foreach (var item in settings.Items)
            {
                SettingRows.Add(new SettingRow(item.Label, item.Value));
            }
        }

        Ai.Reset();
        CaptureChanged?.Invoke(capture);
    }

    private string? BuildPreview(string? note) =>
        Current is null ? null : AiPrompts.BuildSingle(Current.Result, note);

    private Task<AiAnalysis> RunAiAsync(string? note, CancellationToken ct) =>
        Current is null
            ? throw new AiException("Load a capture first.")
            : AppServices.CreateAnalyst().AnalyzeAsync(Current.Result, note, ct);

    private static InfoBarSeverity Map(Finding f) => f.Id == "SMOOTH" ? InfoBarSeverity.Success : f.Severity switch
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



