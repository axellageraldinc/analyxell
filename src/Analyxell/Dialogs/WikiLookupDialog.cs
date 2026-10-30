using Analyxell.Core.Config;
using Analyxell.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Analyxell.Dialogs;

/// <summary>
/// Looks one game up on PCGamingWiki when the user asks. Only the game's name is sent. The result is shown in full
/// first, and nothing is kept unless the user saves it. What it finds is a lead (where the game's settings file is and
/// what the game offers), written by volunteers, never proof of what is switched on.
/// </summary>
internal static class WikiLookupDialog
{
    public static async Task<bool> ShowAsync(XamlRoot root, string gameName, string processName, string installFolder)
    {
        WikiGameInfo? found = null;
        string? resolved = null;

        var nameBox = new TextBox { Header = "Game name to look up", Text = gameName };
        AutomationProperties.SetAutomationId(nameBox, "WikiGameName");
        var go = new Button { Content = "Look it up on PCGamingWiki" };
        AutomationProperties.SetAutomationId(go, "WikiLookupGo");
        var ring = new ProgressRing { Width = 22, Height = 22, IsActive = false, Visibility = Visibility.Collapsed };
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 540 };
        AutomationProperties.SetAutomationId(status, "WikiStatus");
        var result = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 300, MaxWidth = 540, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(result, "WikiResult");

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Look this game up",
            PrimaryButtonText = "Save to the library",
            CloseButtonText = "Cancel",
            IsPrimaryButtonEnabled = false,
            DefaultButton = ContentDialogButton.Close,
        };

        go.Click += async (_, _) =>
        {
            go.IsEnabled = false;
            ring.IsActive = true;
            ring.Visibility = Visibility.Visible;
            status.Text = "Asking PCGamingWiki...";
            try
            {
                found = await new PcGamingWikiClient(AppServices.Http, "0.1").LookUpAsync(nameBox.Text.Trim(), CancellationToken.None);
                if (found is null)
                {
                    status.Text = "PCGamingWiki has no page with usable information for that name. Check the spelling, or add the settings yourself.";
                    result.Visibility = Visibility.Collapsed;
                    return;
                }

                resolved = ConfigPathResolver.Resolve(found.ConfigPaths, installFolder, p => File.Exists(p) || Directory.Exists(p));
                result.Text = Describe(found, resolved);
                result.Visibility = Visibility.Visible;
                status.Text = "Found: " + found.Title + ". Read it below, then save it if it looks right.";
                dialog.IsPrimaryButtonEnabled = processName.Length > 0;
                if (processName.Length == 0)
                {
                    status.Text += " Add the game to the library first (choose its file), then look it up again to save this.";
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                status.Text = "Could not reach PCGamingWiki: " + ex.Message + " Nothing was changed.";
            }
            finally
            {
                ring.IsActive = false;
                ring.Visibility = Visibility.Collapsed;
                go.IsEnabled = true;
            }
        };

        dialog.Content = new StackPanel
        {
            Spacing = 10,
            MinWidth = 520,
            Children =
            {
                new TextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 540,
                    Opacity = 0.8,
                    Text = "This sends only the game's name to pcgamingwiki.com (a community-written wiki) and reads the answer. It finds where the game keeps its settings file and which upscalers, frame generation and VSync options it offers. It cannot tell what is switched on in your copy, or what the numbers in the settings file mean.",
                },
                nameBox,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { go, ring } },
                status,
                result,
            },
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || found is null || processName.Length == 0)
        {
            return false;
        }

        var profile = AppServices.Profiles.Load(processName) ?? new GameProfile { ProcessName = processName, Game = gameName };
        profile.GameFacts = [.. found.Features];
        profile.FactsSource = found.PageUrl;
        profile.ConfigPath = resolved ?? "";
        AppServices.Profiles.Save(profile);
        return true;
    }

    private static string Describe(WikiGameInfo info, string? resolved)
    {
        var lines = new List<string>();
        lines.Add(resolved is not null
            ? "Settings file on this PC: " + resolved
            : info.ConfigPaths.Count > 0
                ? "Settings file (not found on this PC yet): " + string.Join("  or  ", info.ConfigPaths)
                : "The wiki does not say where the settings file is.");
        lines.Add("");
        foreach (var f in info.Features)
        {
            lines.Add($"{Pretty(f.Key)}: {f.Value}");
        }

        lines.Add("");
        lines.Add("Source: " + info.PageUrl);
        return string.Join(Environment.NewLine, lines);
    }

    private static string Pretty(string key)
    {
        var text = key.Replace('_', ' ');
        return char.ToUpperInvariant(text[0]) + text[1..];
    }
}
