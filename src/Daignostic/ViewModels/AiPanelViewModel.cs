using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daignostic.Core.Ai;
using Daignostic.Services;

namespace Daignostic.ViewModels;

public sealed record AiItemView(string Title, string Confidence, string Explanation, string Actions, string Evidence, bool HasActions, bool IsFirst);

/// <summary>
/// Drives the "Ask AI" panel. The model only ever receives the text shown in <see cref="Preview"/>, and nothing is
/// sent until the user presses the button. The AI has no tools, so it cannot read or change anything on this PC.
/// AI is optional: without it the same data can be exported to a file for the user's own AI.
/// While a request runs the panel shows what is happening and how long it has taken, offers Cancel, and says what
/// to do when it is slow. When it fails it says what failed and what to try.
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
        Summary = "";
        DroppedNotice = "";
        ExportStatus = "";
        UnknownsText = "";
        PhaseText = "";
        ElapsedText = "";
        HintText = "";
        ErrorTitle = "";
        ErrorMessage = "";
        ErrorAdvice = "";
        ErrorRaw = "";
    }

    public ObservableCollection<AiItemView> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPreview), nameof(CanAsk), nameof(CanExport))]
    public partial string Preview { get; set; }

    [ObservableProperty]
    public partial string Note { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAsk), nameof(BarStatus), nameof(NotBusy))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string PhaseText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BarStatus))]
    public partial string ElapsedText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    public partial string HintText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string ErrorTitle { get; set; }

    [ObservableProperty]
    public partial string ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string ErrorAdvice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasErrorRaw))]
    public partial string ErrorRaw { get; set; }

    [ObservableProperty]
    public partial bool ErrorNeedsSettings { get; set; }

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
    [NotifyPropertyChangedFor(nameof(HasResult), nameof(BarStatus))]
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

    public bool NotBusy => !IsBusy;

    public bool HasError => ErrorTitle.Length > 0;

    public bool HasErrorRaw => ErrorRaw.Length > 0;

    public bool HasHint => HintText.Length > 0;

    public bool HasSummary => Summary.Length > 0;

    public bool HasDropped => DroppedNotice.Length > 0;

    public bool HasUnknowns => UnknownsText.Length > 0;

    public bool HasResult => HasAnalysis;

    public bool HasExportStatus => ExportStatus.Length > 0;

    /// <summary>One line for the compact bar on the overview.</summary>
    public string BarStatus => IsBusy
        ? $"Asking your AI... {ElapsedText}. Open the Ask AI tab to follow along or cancel."
        : HasAnalysis
            ? $"Answer ready: {Items.Count} point(s). Open the Ask AI tab to read it."
            : "daignostic measured everything on this page. An AI can explain it for your PC and put the fixes in order. It sees measurements only.";

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
        ClearAnswer();
        ClearError();
        ExportStatus = "";
        RefreshPreview();
    }

    /// <summary>Removes the previous answer so an old one is never shown beside a new request or a new error.</summary>
    private void ClearAnswer()
    {
        Items.Clear();
        Analysis = null;
        HasAnalysis = false;
        Summary = "";
        DroppedNotice = "";
        UnknownsText = "";
        IsInconclusive = false;
        OnPropertyChanged(nameof(BarStatus));
    }

    [RelayCommand]
    private void SetupAi() => AppNavigation.GoTo("settings");

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

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
        ClearError();
        ClearAnswer();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var cts = _cts;
        var started = DateTime.UtcNow;
        IsBusy = true;
        ElapsedText = "0:00";
        PhaseText = "Packaging the measurements and findings...";
        HintText = "";
        _ = TickAsync(started, cts.Token);

        try
        {
            var analysis = await _run(string.IsNullOrWhiteSpace(Note) ? null : Note, cts.Token);
            Analysis = analysis;
            Items.Clear();
            var first = true;
            foreach (var item in analysis.Items)
            {
                Items.Add(new AiItemView(
                    item.Title,
                    string.IsNullOrWhiteSpace(item.Confidence) ? "unrated" : item.Confidence,
                    item.Explanation,
                    string.Join(Environment.NewLine, item.Actions.Select(a => "• " + a)),
                    "Evidence: " + string.Join(", ", item.Evidence),
                    item.Actions.Count > 0,
                    first));
                first = false;
            }

            UnknownsText = analysis.Unknowns is { Count: > 0 } u ? string.Join(Environment.NewLine, u.Select(x => "• " + x)) : "";
            Summary = analysis.Summary;
            IsInconclusive = analysis.Inconclusive;
            DroppedNotice = analysis.DroppedUncited > 0
                ? $"{analysis.DroppedUncited} claim(s) were discarded because they cited no measured evidence."
                : "";
            HasAnalysis = true;
            OnPropertyChanged(nameof(BarStatus));
        }
        catch (AiException ex)
        {
            ShowError(ex);
        }
        catch (OperationCanceledException)
        {
            ErrorTitle = "You cancelled the request";
            ErrorMessage = "Nothing was sent back and nothing was changed.";
            ErrorAdvice = "Press Ask AI again whenever you like.";
        }
        finally
        {
            cts.Cancel();
            IsBusy = false;
            PhaseText = "";
            HintText = "";
        }
    }

    /// <summary>Updates the clock and the phase text while a request runs, and says what to do when it is slow.</summary>
    private async Task TickAsync(DateTime started, CancellationToken stop)
    {
        var model = AppServices.Settings.AiModel;
        var limit = AppServices.AiTimeoutSeconds;
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(250, stop);
                var elapsed = DateTime.UtcNow - started;
                ElapsedText = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
                PhaseText = elapsed.TotalSeconds < 1.5
                    ? "Packaging the measurements and findings..."
                    : $"Sent to {model}. Waiting for the answer...";
                HintText = elapsed.TotalSeconds switch
                {
                    >= 100 => $"Giving up in {Math.Max(0, limit - (int)elapsed.TotalSeconds)} seconds if there is no answer.",
                    >= 60 => "This is taking unusually long. You can cancel and try a faster model.",
                    >= 20 => "Still working. Larger models can take a minute or more.",
                    _ => "",
                };
            }
        }
        catch (OperationCanceledException)
        {
            // The request finished or was cancelled; stop ticking.
        }
    }

    private void ShowError(AiException ex)
    {
        ErrorRaw = ex.RawReply is { Length: > 0 } raw ? (raw.Length > 4000 ? raw[..4000] + "\n[cut]" : raw) : "";
        ErrorNeedsSettings = ex.Kind is AiErrorKind.Endpoint or AiErrorKind.Key or AiErrorKind.Model;
        (ErrorTitle, ErrorAdvice) = ex.Kind switch
        {
            AiErrorKind.Endpoint => ("Could not reach the AI", "Check that the service is running and that the address in Settings is right. A local gateway has to be started first."),
            AiErrorKind.Key => ("The AI did not accept your API key", "Open Settings and re-enter the key. Check that it is for this service and has not expired."),
            AiErrorKind.Model => ("The AI did not accept the address or the model name", "In Settings, press List models and pick a name from the list. The address works with or without /v1."),
            AiErrorKind.Timeout => ("The AI took too long", "Try again, or choose a faster model in Settings. Very long answers from large models can exceed the limit."),
            AiErrorKind.Reply => ("The AI answered, but not in a form daignostic can read", "The model did not follow the required format. Try again, or use a stronger model. You can read exactly what it said below."),
            _ => ("The AI request failed", "Try again. If it keeps happening, check Settings."),
        };
        ErrorMessage = ex.Message;
    }

    private void ClearError()
    {
        ErrorTitle = "";
        ErrorMessage = "";
        ErrorAdvice = "";
        ErrorRaw = "";
        ErrorNeedsSettings = false;
    }
}
