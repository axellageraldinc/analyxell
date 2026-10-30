using Analyxell.Core.Config;
using Analyxell.Core.SystemInfo;
using Analyxell.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Analyxell.Dialogs;

/// <summary>
/// The one place to tell analyxell about a game. Three optional steps, each with a plain status line: where the game is
/// installed, what it offers (an online lookup the user starts), and its settings. Every route into setting up a game
/// (the Game library, the Analyze page) opens this same screen.
///
/// A window can show only one dialog at a time, so a step closes this screen, runs, and the screen opens again with
/// the new status.
/// </summary>
internal static class GameSetupDialog
{
    private enum Step
    {
        Done,
        Folder,
        Lookup,
        Settings,
    }

    /// <param name="editSettings">Opens the settings editor that fits where the user came from (this capture, or the game in general).</param>
    /// <returns>True when anything was saved.</returns>
    public static async Task<bool> RunAsync(
        XamlRoot root,
        string gameName,
        string processName,
        string installHint,
        Func<GameProfile, Task<bool>> editSettings)
    {
        var profile = AppServices.Profiles.FindFor(processName, gameName)
            ?? new GameProfile { ProcessName = processName, Game = gameName };
        if (profile.InstallPath.Length == 0)
        {
            profile.InstallPath = installHint.Length > 0 ? installHint : GameLocator.FindSteamInstall(AppServices.Probe, gameName) ?? "";
        }

        var changed = false;
        while (true)
        {
            var step = await ShowAsync(root, profile);
            if (step == Step.Done)
            {
                return changed;
            }

            switch (step)
            {
                case Step.Folder when await Pickers.PickFolderAsync() is { } folder:
                    profile.InstallPath = folder;
                    AppServices.Profiles.Save(profile);
                    changed = true;
                    break;
                case Step.Lookup when await WikiLookupDialog.ShowAsync(root, profile):
                    changed = true;
                    break;
                case Step.Settings when await editSettings(profile):
                    changed = true;
                    break;
            }

            // The other steps may have saved the profile; read it back so the status lines are current.
            profile = AppServices.Profiles.FindFor(profile.ProcessName, profile.Game) ?? profile;
        }
    }

    private static async Task<Step> ShowAsync(XamlRoot root, GameProfile profile)
    {
        var chosen = Step.Done;
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Set up " + profile.Game,
            CloseButtonText = "Done",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.Resources["ContentDialogMaxWidth"] = 760.0;

        StackPanel Row(string number, string title, string status, string button, Step step, string id)
        {
            var action = new Button { Content = button, MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(action, id);
            action.Click += (_, _) =>
            {
                chosen = step;
                dialog.Hide();
            };
            var text = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = number + "  " + title, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 12, MaxWidth = 430 },
                },
            };
            var grid = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 8, 0, 8) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(text);
            Grid.SetColumn(action, 1);
            grid.Children.Add(action);
            return new StackPanel { Children = { grid } };
        }

        var installed = profile.InstallPath.Length > 0
            ? "Installed in " + profile.InstallPath + ". This tells analyxell what kind of drive the game runs from."
            : "Not set. Optional: it lets analyxell see whether the game is on an SSD or a hard disk, and how full the drive is.";
        var offers = profile.GameFacts.Count > 0
            ? "Done. The game offers: " + string.Join("; ", profile.GameFacts.Where(f => f.Key is "upscaling" or "frame_generation" or "vsync_modes").Select(f => $"{f.Key.Replace('_', ' ')} {f.Value}"))
            : "Not looked up yet. Finds where the game keeps its settings file and which upscaling, frame generation and VSync options it has.";
        var settings = profile.Items.Count > 0
            ? $"{profile.Items.Count} setting(s) known. You can change them."
            : "None yet. Optional: add them so the findings and the AI can take them into account.";

        dialog.Content = new StackPanel
        {
            Spacing = 4,
            Width = 640,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.8,
                    MaxWidth = 640,
                    Text = "Every step is optional, and analyxell works without them. Doing them makes the findings, and the AI's answers, more accurate for this game.",
                },
                Row("1", "Where the game is installed", installed, "Choose folder...", Step.Folder, "SetupStepFolder"),
                Row("2", "What the game offers", offers, "Look up online...", Step.Lookup, "SetupStepLookup"),
                Row("3", "The game's settings", settings, "Add or change...", Step.Settings, "SetupStepSettings"),
            },
        };

        await dialog.ShowAsync();
        return chosen;
    }
}
