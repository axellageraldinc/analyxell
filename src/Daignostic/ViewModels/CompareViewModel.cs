using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daignostic.Core.Ai;
using Daignostic.Core.Analysis;
using Daignostic.Services;

namespace Daignostic.ViewModels;

public sealed record CompareRowItem(string Label, string A, string B, string Delta, string Glyph, string Verdict);

public sealed partial class CompareViewModel : ObservableObject
{
    public CompareViewModel()
    {
        Error = "";
        Summary = "Pick a baseline (A) and a later run (B). Load captures on the Analyze page or with the buttons here.";
        Ai = new AiPanelViewModel(RunAiAsync, BuildPreview);
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

    /// <summary>Raised when the overlay chart should be redrawn (both captures set) or cleared (null).</summary>
    public event Action<ComparisonResult?>? ComparisonChanged;

    public ComparisonResult? Comparison { get; private set; }

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

    partial void OnAChanged(LoadedCapture? value) => Recompute();

    partial void OnBChanged(LoadedCapture? value) => Recompute();

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
        if (!CanCompare)
        {
            Summary = "Pick two different captures: a baseline (A) and a later run (B).";
            ComparisonChanged?.Invoke(null);
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

        Summary = cmp.WorsenedCount == 0 && cmp.ImprovedCount > 0
            ? $"B is better on {cmp.ImprovedCount} of {cmp.ComparedCount} measures and worse on none."
            : $"B is better on {cmp.ImprovedCount} and worse on {cmp.WorsenedCount} of {cmp.ComparedCount} measures.";

        OnPropertyChanged(nameof(HasRows));
        Ai.Reset();
        ComparisonChanged?.Invoke(cmp);
    }

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


