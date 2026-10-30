using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daignostic.Core.Ai;
using Daignostic.Core.Analysis;
using Daignostic.Services;
using Microsoft.UI.Xaml.Controls;

namespace Daignostic.ViewModels;

public sealed record CompareRowItem(string Label, string A, string B, string Delta, string Glyph, string Verdict);

public sealed partial class CompareViewModel : ObservableObject
{
    public CompareViewModel()
    {
        Error = "";
        Summary = "Pick a baseline (A) and a later run (B). Load captures on the Analyze page or with the buttons here.";
        Ai = new AiPanelViewModel(RunAiAsync, BuildPreview, BuildPack, "daignostic-comparison-ai-pack");
        if (AppState.Loaded.Count >= 2)
        {
            B = AppState.Loaded[0];
            A = AppState.Loaded[1];
        }
        else if (AppState.Loaded.Count == 1)
        {
            B = AppState.Loaded[0];
        }
    }

    public ObservableCollection<LoadedCapture> Loaded => AppState.Loaded;

    public ObservableCollection<CompareRowItem> Rows { get; } = [];

    public AiPanelViewModel Ai { get; }

    public ObservableCollection<ChartViewItem> ChartViews { get; } = [];

    /// <summary>Raised when the overlay chart should be redrawn, or cleared (null spec). The freezes are run B's.</summary>
    public event Action<ChartSpec?, IReadOnlyList<double>>? ChartChanged;

    public ComparisonResult? Comparison { get; private set; }

    [ObservableProperty]
    public partial ChartViewItem? SelectedChartView { get; set; }

    [ObservableProperty]
    public partial string ChartTitle { get; set; } = "";

    [ObservableProperty]
    public partial string ChartCaption { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    public partial LoadedCapture? A { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompare))]
    public partial LoadedCapture? B { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; }

    public bool CanCompare => A is not null && B is not null && !ReferenceEquals(A, B);

    public bool HasError => Error.Length > 0;

    public bool HasRows => Rows.Count > 0;

    /// <summary>What differed between the runs besides the PC, so a gain is not credited to the wrong thing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasChangesNote))]
    public partial string ChangesNote { get; set; } = "";

    [ObservableProperty]
    public partial string ChangesTitle { get; set; } = "";

    [ObservableProperty]
    public partial InfoBarSeverity ChangesSeverity { get; set; } = InfoBarSeverity.Informational;

    public bool HasChangesNote => ChangesNote.Length > 0;

    partial void OnAChanged(LoadedCapture? value) => Recompute();

    partial void OnBChanged(LoadedCapture? value) => Recompute();

    partial void OnSelectedChartViewChanged(ChartViewItem? value) => RaiseChart();

    /// <summary>Redraws the overlay for the selected metric, or clears it when there is nothing to compare.</summary>
    public void RaiseChart()
    {
        if (Comparison is null || A is null || B is null || SelectedChartView is null)
        {
            ChartChanged?.Invoke(null, []);
            return;
        }

        var spec = ChartDataBuilder.BuildComparison(A.Result.Run, B.Result.Run, SelectedChartView.View, A.Name, B.Name);
        ChartTitle = $"{spec.Label} ({spec.Unit})";
        ChartCaption = spec.Caption + " Red markers are B's freezes.";
        ChartChanged?.Invoke(spec, B.Result.Freezes.Select(f => f.TimeSec).ToList());
    }

    private void RefreshChartViews()
    {
        var previous = SelectedChartView?.View;
        ChartViews.Clear();
        if (A is not null && B is not null && CanCompare)
        {
            foreach (var view in ChartDataBuilder.AvailableForComparison(A.Result.Run, B.Result.Run))
            {
                ChartViews.Add(new ChartViewItem(view, ChartDataBuilder.BuildComparison(A.Result.Run, B.Result.Run, view, A.Name, B.Name).Label));
            }
        }

        SelectedChartView = ChartViews.FirstOrDefault(c => c.View == previous) ?? ChartViews.FirstOrDefault();
    }

    [RelayCommand]
    private Task LoadAAsync() => LoadIntoAsync(isA: true);

    [RelayCommand]
    private Task LoadBAsync() => LoadIntoAsync(isA: false);

    private async Task LoadIntoAsync(bool isA)
    {
        var path = await Pickers.PickCaptureAsync();
        if (path is null)
        {
            return;
        }

        try
        {
            var loaded = await AppState.LoadAsync(path);
            if (isA)
            {
                A = loaded;
            }
            else
            {
                B = loaded;
            }

            Error = "";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
        {
            Error = "That file is not a readable CapFrameX capture.";
        }
    }

    private void Recompute()
    {
        Rows.Clear();
        Comparison = null;
        ChangesNote = "";
        ChangesTitle = "";
        if (!CanCompare)
        {
            Summary = "Pick two different captures: a baseline (A) and a later run (B).";
            RefreshChartViews();
            RaiseChart();
            OnPropertyChanged(nameof(HasRows));
            Ai.Reset();
            return;
        }

        var cmp = RunComparer.Compare(A!.Result, B!.Result, A.Name, B.Name);
        Comparison = cmp;
        foreach (var row in cmp.Rows)
        {
            Rows.Add(new CompareRowItem(
                row.Label,
                Fmt(row.A, row.Unit),
                Fmt(row.B, row.Unit),
                DeltaText(row),
                string.Empty,
                row.HigherIsBetter is null
                    ? "Context"
                    : row.Improved switch { true => "\u25B2 Better", false => "\u25BC Worse", _ => "= Same" }));
        }

        if (cmp.Changes.Count > 0)
        {
            ChangesTitle = cmp.ChangesAffectFrameRate
                ? "The runs used different settings, so part of the difference is the settings, not your PC"
                : "The runs used different settings";
            ChangesNote = string.Join(Environment.NewLine, cmp.Changes.Select(c => $"\u2022 {c.Label}: {c.A} \u2192 {c.B}"));
            ChangesSeverity = cmp.ChangesAffectFrameRate ? InfoBarSeverity.Warning : InfoBarSeverity.Informational;
        }
        else if (cmp.SettingsUnknown)
        {
            ChangesTitle = "daignostic does not know the game settings of one or both runs";
            ChangesNote = "If you changed a setting between the runs (for example DLSS, frame generation or ray tracing), say so in the note for the AI, or enter the settings for each capture on the Analyze page.";
            ChangesSeverity = InfoBarSeverity.Informational;
        }
        else
        {
            ChangesTitle = "The game settings are the same in both runs";
            ChangesNote = "Any difference below comes from something other than these settings.";
            ChangesSeverity = InfoBarSeverity.Success;
        }

        Summary = cmp.WorsenedCount == 0 && cmp.ImprovedCount > 0
            ? $"B is better on {cmp.ImprovedCount} of {cmp.ComparedCount} measures and worse on none."
            : $"B is better on {cmp.ImprovedCount} and worse on {cmp.WorsenedCount} of {cmp.ComparedCount} measures.";

        OnPropertyChanged(nameof(HasRows));
        Ai.Reset();
        RefreshChartViews();
        RaiseChart();
    }

    private string? BuildPack(string? note) =>
        Comparison is null ? null : AiExportPack.BuildComparison(Comparison, A!.Result, B!.Result, note, DateTime.UtcNow);

    private string? BuildPreview(string? note) =>
        Comparison is null ? null : AiPrompts.BuildComparison(Comparison, A!.Result, B!.Result, note);

    private Task<AiAnalysis> RunAiAsync(string? note, CancellationToken ct) =>
        Comparison is null
            ? throw new AiException("Pick two captures first.")
            : AppServices.CreateAnalyst().CompareAsync(Comparison, A!.Result, B!.Result, note, ct);

    private static string Fmt(double v, string unit) =>
        v.ToString(Math.Abs(v) >= 100 ? "0" : "0.#", CultureInfo.InvariantCulture) + (unit.Length > 0 ? " " + unit : "");

    private static string DeltaText(ComparisonRow row)
    {
        var sign = row.Delta > 0 ? "+" : "";
        var text = sign + row.Delta.ToString("0.#", CultureInfo.InvariantCulture);
        return row.PercentDelta is { } p ? $"{text} ({sign}{p.ToString("0", CultureInfo.InvariantCulture)}%)" : text;
    }
}


