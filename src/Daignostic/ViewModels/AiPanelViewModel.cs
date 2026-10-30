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
/// </summary>
public sealed partial class AiPanelViewModel : ObservableObject
{
    private readonly Func<string?, CancellationToken, Task<AiAnalysis>> _run;
    private readonly Func<string?, string?> _buildPreview;
    private CancellationTokenSource? _cts;

    public AiPanelViewModel(Func<string?, CancellationToken, Task<AiAnalysis>> run, Func<string?, string?> buildPreview)
    {
        _run = run;
        _buildPreview = buildPreview;
        Preview = "";
        Note = "";
        Error = "";
        Summary = "";
        DroppedNotice = "";
    }

    public ObservableCollection<AiItemView> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview))]
    [NotifyPropertyChangedFor(nameof(CanAsk))]
    public partial string Preview { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AskCommand))]
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
    public partial bool IsInconclusive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    public partial bool HasAnalysis { get; set; }

    public AiAnalysis? Analysis { get; private set; }

    public string Destination => $"{AppServices.Settings.AiEndpoint} (preset \"{AppServices.Settings.AiPreset}\")";

    public bool HasPreview => Preview.Length > 0;

    public bool CanAsk => HasPreview && !IsBusy;

    public bool HasError => Error.Length > 0;

    public bool HasSummary => Summary.Length > 0;

    public bool HasDropped => DroppedNotice.Length > 0;

    public bool HasResult => HasAnalysis;

    partial void OnPreviewChanged(string value) => AskCommand.NotifyCanExecuteChanged();

    partial void OnNoteChanged(string value) => RefreshPreview();

    /// <summary>Rebuilds the preview of what would be sent. Call whenever the underlying data changes.</summary>
    public void RefreshPreview()
    {
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
        IsInconclusive = false;
        RefreshPreview();
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



