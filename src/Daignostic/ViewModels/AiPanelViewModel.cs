using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Daignostic.Core.Ai;
using Daignostic.Services;

namespace Daignostic.ViewModels;

/// <summary>One thing the AI could not tell, with a box for the user's own answer.</summary>
public sealed partial class AiUnknownView(string text, Action<string, string> changed) : ObservableObject
{
    public string Text { get; } = text;

    [ObservableProperty]
    public partial string Answer { get; set; } = "";

    partial void OnAnswerChanged(string value) => changed(Text, value);
}

public sealed record AiCheckView(string Id, string Title, string Why);

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
    private readonly Func<IReadOnlyList<AiCheck>>? _availableChecks;
    private CancellationTokenSource? _cts;

    public AiPanelViewModel(
        Func<string?, CancellationToken, Task<AiAnalysis>> run,
        Func<string?, string?> buildPreview,
        Func<string?, string?> buildPack,
        string packName,
        Func<IReadOnlyList<AiCheck>>? availableChecks = null)
    {
        _availableChecks = availableChecks;
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

    /// <summary>Checks the AI said would help. Pressing one runs it here, in daignostic; the AI never runs anything.</summary>
    public ObservableCollection<AiCheckView> Checks { get; } = [];

    /// <summary>What the AI could not tell, each with room for the user to answer. Answers go into the next question.</summary>
    public ObservableCollection<AiUnknownView> UnknownItems { get; } = [];

    private readonly Dictionary<string, string> _answers = [];

    /// <summary>The user's note, followed by their answers to the AI's open questions. Null when both are empty.</summary>
    private string? EffectiveNote()
    {
        var answered = _answers.Where(a => !string.IsNullOrWhiteSpace(a.Value)).ToList();
        var note = Note.Trim();
        if (answered.Count == 0)
        {
            return note.Length == 0 ? null : note;
        }

        var lines = answered.Select(a => $"- {a.Key}: {a.Value.Trim()}");
        return (note.Length > 0 ? note + "\n\n" : "") + "Answers to your open questions:\n" + string.Join("\n", lines);
    }

    private void OnUnknownAnswered(string question, string answer)
    {
        _answers[question] = answer;
        RefreshPreview();
    }

    /// <summary>Raised when the user presses one of <see cref="Checks"/>. The page runs it (a form or a read-only check).</summary>
    public event Action<string>? CheckRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCheckStatus))]
    public partial string CheckStatus { get; set; } = "";

    public bool HasCheckStatus => CheckStatus.Length > 0;

    /// <summary>True when the data changed after the answer was given, so the answer no longer covers everything.</summary>
    [ObservableProperty]
    public partial bool IsStale { get; set; }

    public bool HasChecks => Checks.Count > 0;

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
        Preview = _buildPreview(EffectiveNote()) ?? "";
        OnPropertyChanged(nameof(Destination));
    }

    public void Reset()
    {
        _answers.Clear();
        ClearAnswer();
        ClearError();
        ExportStatus = "";
        RefreshPreview();
    }

    /// <summary>Removes the previous answer so an old one is never shown beside a new request or a new error.</summary>
    private void ClearAnswer()
    {
        Items.Clear();
        UnknownItems.Clear();
        Checks.Clear();
        OnPropertyChanged(nameof(HasChecks));
        IsStale = false;
        CheckStatus = "";
        Analysis = null;
        HasAnalysis = false;
        Summary = "";
        DroppedNotice = "";
        UnknownsText = "";
        IsInconclusive = false;
        OnPropertyChanged(nameof(BarStatus));
    }

    /// <summary>Keeps the answer on screen but says it is out of date, after the user added information.</summary>
    public void MarkStale()
    {
        if (HasAnalysis)
        {
            IsStale = true;
            Checks.Clear();
            OnPropertyChanged(nameof(HasChecks));
        }

        RefreshPreview();
    }

    public void RequestCheck(string id) => CheckRequested?.Invoke(id);

    [RelayCommand]
    private void SetupAi() => AppNavigation.GoTo("settings");

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportPackAsync()
    {
        var pack = _buildPack(EffectiveNote());
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
            var analysis = await _run(EffectiveNote(), cts.Token);
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

            Checks.Clear();
            var offered = _availableChecks?.Invoke() ?? [];
            foreach (var id in analysis.NextChecks ?? [])
            {
                if (offered.FirstOrDefault(c => c.Id == id) is { } check)
                {
                    Checks.Add(new AiCheckView(check.Id, check.Title, check.Why));
                }
            }

            OnPropertyChanged(nameof(HasChecks));
            CheckStatus = "";
            foreach (var unknown in analysis.Unknowns ?? [])
            {
                UnknownItems.Add(new AiUnknownView(unknown, OnUnknownAnswered) { Answer = _answers.GetValueOrDefault(unknown, "") });
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
        ErrorNeedsSettings = AiErrorText.Describe(ex).NeedsSettings;
        var described = AiErrorText.Describe(ex);
        (ErrorTitle, ErrorAdvice) = (described.Title, described.Advice + (ErrorRaw.Length > 0 ? " You can read exactly what it said below." : ""));
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
