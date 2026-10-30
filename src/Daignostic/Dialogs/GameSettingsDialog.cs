using Daignostic.Core.Ai;
using Daignostic.Core.Config;
using Daignostic.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Daignostic.Dialogs;

/// <summary>
/// Gives daignostic a game's graphics settings when it has no built-in reader: type or paste them, or let the user's
/// own AI pick them out of the game's config file. AI results are only kept if each key and value literally appears
/// in the file, the file text is cleaned of likely secrets first, and the user sees everything before it is sent.
/// </summary>
internal static class GameSettingsDialog
{
    /// <summary>Settings for one capture: saved for that capture, and remembered for the game to pre-fill next time.</summary>
    public static Task<bool> ShowAsync(XamlRoot root, LoadedCapture capture)
    {
        var profile = AppServices.Profiles.Load(capture.ProcessName) ?? new GameProfile { ProcessName = capture.ProcessName, Game = capture.Result.Info.Game };
        var existing = AppServices.CaptureSettings.Load(CaptureSettingsResolver.KeyFor(capture.File.Info));
        var items = existing is { Items.Count: > 0 } && existing.Items.All(i => i.Key.StartsWith("profile.", StringComparison.Ordinal))
            ? existing.Items.Select(i => new ProfileItem(i.Label, i.Value)).ToList()
            : new List<ProfileItem>(profile.Items);
        return ShowCoreAsync(root, capture.Result.Info.Game, profile, items, (saved, origin) =>
        {
            // The settings belong to this capture only; the game profile just pre-fills the form next time.
            var forCapture = new GameSettings(
                capture.Result.Info.Game,
                "entered by you",
                DateTime.UtcNow,
                [.. saved.Select(i => new GameSetting("profile." + i.Key, i.Key, i.Value, IsRaw: true, Source: origin))]);
            AppState.SaveCaptureSettings(capture, forCapture);
        });
    }

    /// <summary>Settings for a game in the Library: they apply to captures recorded after they were saved.</summary>
    public static Task<bool> ShowForGameAsync(XamlRoot root, GameProfile profile) =>
        ShowCoreAsync(root, profile.Game, profile, new List<ProfileItem>(profile.Items), null);

    private static async Task<bool> ShowCoreAsync(XamlRoot root, string gameName, GameProfile profile, List<ProfileItem> items, Action<List<ProfileItem>, string>? afterSave)
    {
        var origin = string.IsNullOrWhiteSpace(profile.Origin) ? "entered by you" : profile.Origin;
        var list = new ListView { MaxHeight = 160, SelectionMode = ListViewSelectionMode.None };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = $"Settings for {gameName}",
            PrimaryButtonText = "Save these settings",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        void Refresh()
        {
            list.ItemsSource = items.Select(i => $"{i.Key} = {i.Value}").ToList();
            dialog.IsPrimaryButtonEnabled = items.Count > 0;
        }

        // Tab 1: type or paste.
        var paste = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            Height = 130,
            PlaceholderText = "Paste JSON, or lines like  ShadowQuality=High  or  DLSS: Quality",
            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
        };
        AutomationProperties.SetAutomationId(paste, "GameSettingsPasteBox");
        var read = new Button { Content = "Read these settings" };
        AutomationProperties.SetAutomationId(read, "GameSettingsReadButton");
        read.Click += (_, _) =>
        {
            var parsed = FreeFormSettingsParser.Parse(paste.Text);
            if (parsed.Items.Count > 0)
            {
                items = parsed.Items.ToList();
                origin = "entered by you";
            }

            status.Text = $"{parsed.Items.Count} setting(s) read. " + string.Join(" ", parsed.Warnings.Take(2));
            Refresh();
        };
        var pasteTab = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { paste, read } };

        // Tab 2: import from a config file with the user's AI.
        RedactedText? redacted = null;
        string fileName = "";
        var preview = new TextBox { IsReadOnly = true, AcceptsReturn = true, Height = 130, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"), FontSize = 12, TextWrapping = TextWrapping.NoWrap };
        var send = new Button { Content = "Send this to my AI and extract the settings", IsEnabled = false };
        AutomationProperties.SetAutomationId(send, "GameSettingsAiButton");
        var choose = new Button { Content = "Choose the game's config file..." };

        // Progress for the AI request: what it is doing, how long, and a way out.
        var ring = new ProgressRing { Width = 22, Height = 22, IsActive = false };
        var waiting = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var cancel = new Button { Content = "Cancel" };
        AutomationProperties.SetAutomationId(cancel, "GameSettingsAiCancel");
        AutomationProperties.SetAutomationId(waiting, "GameSettingsAiProgress");
        var progress = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Visibility = Visibility.Collapsed, Children = { ring, waiting, cancel } };
        CancellationTokenSource? importCts = null;
        cancel.Click += (_, _) => importCts?.Cancel();
        var aiOk = AppServices.Settings.AiConfigured;
        var aiNote = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Text = aiOk
                ? "Pick the game's settings file (for example a .ini or .json in its AppData or install folder). Likely secrets and your user name are removed first. You see exactly what will be sent."
                : "This needs an AI. Turn one on in Settings, or use the other tab to type the settings yourself.",
        };
        choose.IsEnabled = aiOk;
        choose.Click += async (_, _) =>
        {
            var path = await Pickers.PickAnyFileAsync();
            if (path is null)
            {
                return;
            }

            try
            {
                var info = new FileInfo(path);
                if (info.Length > 2 * 1024 * 1024)
                {
                    status.Text = "That file is larger than 2 MB, which is too big to be a settings file.";
                    return;
                }

                fileName = info.Name;
                redacted = AiSettingsImporter.Redact(await File.ReadAllTextAsync(path));
                preview.Text = redacted.Text;
                status.Text = $"Ready to send {redacted.Text.Length:N0} characters. {redacted.LinesRemoved} line(s) removed as possible secrets, {redacted.PathsAnonymised} user path(s) anonymised." + (redacted.Truncated ? " The file was long and was cut." : "");
                send.IsEnabled = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                status.Text = "The file could not be read: " + ex.Message;
            }
        };
        send.Click += async (_, _) =>
        {
            if (redacted is null)
            {
                return;
            }

            send.IsEnabled = false;
            status.Text = "";
            importCts = new CancellationTokenSource();
            var cts = importCts;
            var started = DateTime.UtcNow;
            ring.IsActive = true;
            progress.Visibility = Visibility.Visible;
            var model = AppServices.Settings.AiModel;
            waiting.Text = AiErrorText.Waiting(TimeSpan.Zero, model);
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!cts.IsCancellationRequested)
                    {
                        await Task.Delay(500, cts.Token);
                        var text = AiErrorText.Waiting(DateTime.UtcNow - started, model);
                        dialog.DispatcherQueue.TryEnqueue(() => waiting.Text = text);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Finished or cancelled: stop updating.
                }
            });

            try
            {
                var result = await AiSettingsImporter.ImportAsync(AppServices.CreateOpenAiClient(), gameName, redacted, cts.Token);
                if (result.Accepted.Count > 0)
                {
                    items = result.Accepted.ToList();
                    origin = $"AI import from {fileName}, checked against the file";
                }

                status.Text = $"{result.Accepted.Count} setting(s) found. {result.Dropped} discarded because they did not appear in the file. Check them below before saving.";
                Refresh();
            }
            catch (AiException ex)
            {
                var described = AiErrorText.Describe(ex);
                status.Text = $"{described.Title}. {ex.Message} {described.Advice}";
            }
            catch (OperationCanceledException)
            {
                status.Text = "You cancelled the request. Nothing was changed.";
            }
            finally
            {
                cts.Cancel();
                ring.IsActive = false;
                progress.Visibility = Visibility.Collapsed;
                send.IsEnabled = true;
            }
        };
        var importTab = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { aiNote, choose, preview, send, progress } };

        var pivot = new Pivot { Height = 330 };
        pivot.Items.Add(new PivotItem { Header = "Type or paste", Content = pasteTab });
        pivot.Items.Add(new PivotItem { Header = "Import with my AI", Content = importTab });

        var root2 = new StackPanel { Spacing = 8, MinWidth = 600 };
        root2.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "daignostic can read a few games' settings by itself. For any other game, give it the settings here. They are saved on this PC and shown as the game's own numbers without guessing what they mean.",
        });
        root2.Children.Add(pivot);
        root2.Children.Add(new TextBlock { Text = "Settings that will be saved", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        root2.Children.Add(list);
        root2.Children.Add(status);
        dialog.Content = new ScrollViewer { Content = root2, MaxHeight = 620 };
        Refresh();

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        profile.Items = items;
        profile.Origin = origin;
        AppServices.Profiles.Save(profile);

        afterSave?.Invoke(items, origin);
        return true;
    }
}
