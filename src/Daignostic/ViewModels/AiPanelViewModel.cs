using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daignostic.Core.Ai;
using Daignostic.Services;

namespace Daignostic.ViewModels;

public sealed record AiItemView(string Title, string Confidence, string Explanation, string Actions, string Evidence, bool HasActions);

/// <summary>
/// Drives the "Ask AI" panel. The model only ever receives the text shown in <see cref="Preview"/>, and nothing is
/// sent until the user presses the button. The AI has no tools, so it cannot read or change anything on this PC.
/// AI is optional: without it the same data can be exported to a file for the user's own AI.
/// </summary>
public sealed partial class AiPanelViewModel : ObservableObject
{
    private readonly Func<string?, CancellationToken, Task<AiAnalysis>> _run;
    private readonly Func<string?, string?> _buildPreview;
    private readonly Func<string?, string?> _buildPack;
    private readonly string _packName;
    private CancellationTokenSource? _cts;

    public AiPanelViewModel(
        Func<string?, CancellationToken, Task<AiAnalysis>> run,
        Func<string?, string?> buildPreview,
        Func<string?, string?> buildPack,
        string packName)
    {
        _run = run;
        _buildPreview = buildPreview;
        _buildPack = buildPack;
        _packName = packName;
        Preview = "";
        Note = "";
        Error = "";
        Summary = "";
        DroppedNotice = "";
        ExportStatus = "";
        UnknownsText = "";
    }

    public ObservableCollection<AiItemView> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(CanAsk), nameof(CanExport))]
    public partial string Preview { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAsk))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    public partial string Summary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDropped))]
    public partial string DroppedNotice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnknowns))]
    public partial string UnknownsText { get; set; }

    [ObservableProperty]
    public partial bool IsInconclusive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial bool HasAnalysis { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExportStatus))]
    public partial string ExportStatus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAsk), nameof(NeedsSetup))]
    public partial bool IsConfigured { get; set; }

    public AiAnalysis? Analysis { get; private set; }

    public string Destination => IsConfigured ? $"{AppServices.Settings.AiEndpoint} (model \"{AppServices.Settings.AiModel}\")" : "no AI configured";

    public bool HasPreview => Preview.Length > 0;

    public bool CanAsk => HasPreview && !IsBusy && IsConfigured;

    public bool CanExport => HasPreview;

    public bool NeedsSetup => !IsConfigured;

    public bool HasError => Error.Length > 0;

    public bool HasSummary => Summary.Length > 0;

    public bool HasDropped => DroppedNotice.Length > 0;

    public bool HasResult => HasAnalysis;

    public bool HasUnknowns => UnknownsText.Length > 0;

    public bool HasExportStatus => ExportStatus.Length > 0;

    partial void OnPreviewChanged(string value)
    {
        AskCommand.NotifyCanExecuteChanged();
        ExportPackCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsConfiguredChanged(bool value) => AskCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => AskCommand.NotifyCanExecuteChanged();

    partial void OnNoteChanged(string value) => RefreshPreview();

    /// <summary>Rebuilds the preview and re-reads the AI settings. Call whenever the data or the settings change.</summary>
    public void RefreshPreview()
    {
        IsConfigured = AppServices.Settings.AiConfigured;
        Preview = _buildPreview(string.IsNullOrWhiteSpace(Note) ? null : Note) ?? "";
        OnPropertyChanged(nameof(Destination));
    }

    public void Reset()
    {
        Items.Clear();
        Analysis = null;
        HasAnalysis = false;
        Summary = "";
        DroppedNotice = "";
        Error = "";
        ExportStatus = "";
        UnknownsText = "";
        IsInconclusive = false;
        RefreshPreview();
    }

    [RelayCommand]
    private void SetupAi() => AppNavigation.GoTo("settings");

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportPackAsync()
    {
        var pack = _buildPack(string.IsNullOrWhiteSpace(Note) ? null : Note);
        if (pack is null)
        {
            return;
        }

        var path = await Pickers.PickReportPathAsync(_packName);
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(path, pack);
            ExportStatus = "Saved. Give this file to any AI you like. It contains measurements only: no paths, computer name or keys.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ExportStatus = "The file could not be saved: " + ex.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanAsk))]
    private async Task AskAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        IsBusy = true;
        Error = "";
        try
        {
            var analysis = await _run(string.IsNullOrWhiteSpace(Note) ? null : Note, _cts.Token);
            Analysis = analysis;
            Items.Clear();
            foreach (var item in analysis.Items)
            {
                Items.Add(new AiItemView(
                    item.Title,
                    string.IsNullOrWhiteSpace(item.Confidence) ? "unrated" : item.Confidence,
                    item.Explanation,
                    string.Join(Environment.NewLine, item.Actions.Select(a => "• " + a)),
                    "Evidence: " + string.Join(", ", item.Evidence),
                    item.Actions.Count > 0));
            }

            UnknownsText = analysis.Unknowns is { Count: > 0 } u ? string.Join(Environment.NewLine, u.Select(x => "\u2022 " + x)) : "";
            Summary = analysis.Summary;
            IsInconclusive = analysis.Inconclusive;
            DroppedNotice = analysis.DroppedUncited > 0
                ? $"{analysis.DroppedUncited} claim(s) were discarded because they cited no measured evidence."
                : "";
            HasAnalysis = true;
        }
        catch (AiException ex)
        {
            Error = ex.Message;
        }
        catch (OperationCanceledException)
        {
            Error = "The request was cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
