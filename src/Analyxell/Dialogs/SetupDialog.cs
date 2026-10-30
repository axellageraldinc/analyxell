using Analyxell.Core.Config;
using Analyxell.Core.SystemInfo;
using Analyxell.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Analyxell.Dialogs;

/// <summary>
/// Collects what Windows and the GPU driver do not let analyxell read safely: G-SYNC or FreeSync state, driver
/// options, the monitor, and where the game is installed. Everything is optional and used as entered.
/// </summary>
internal static class SetupDialog
{
    public static async Task<bool> ShowAsync(XamlRoot root, LoadedCapture capture)
    {
        var saved = AppServices.Setup.Load();
        var profile = AppServices.Profiles.Load(capture.ProcessName) ?? new GameProfile { ProcessName = capture.ProcessName, Game = capture.Result.Info.Game };
        var controls = new Dictionary<string, FrameworkElement>();

        var panel = new StackPanel { Spacing = 12, MinWidth = 520 };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = "These are things Windows and your GPU driver do not let analyxell read. Fill in what you know; leave the rest as Unknown. " +
                   "Your answers are used exactly as given and are never verified. They stay on this PC and are only sent to an AI if you ask it.",
        });

        foreach (var field in SetupFields.All)
        {
            saved.Values.TryGetValue(field.Key, out var current);
            FrameworkElement input;
            if (field.Options.Length > 0)
            {
                var combo = new ComboBox { Header = field.Label, HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var option in field.Options)
                {
                    combo.Items.Add(option);
                }

                combo.SelectedItem = field.Options.Contains(current ?? "") ? current : field.Options[0];
                input = combo;
            }
            else
            {
                input = new TextBox { Header = field.Label, Text = current ?? "", PlaceholderText = field.Hint };
            }

            AutomationProperties.SetAutomationId(input, "Setup_" + field.Key);
            controls[field.Key] = input;
            panel.Children.Add(input);
            panel.Children.Add(new TextBlock { Text = field.Hint, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, -8, 0, 0) });
        }

        var installBox = new TextBox { Header = "Where the game is installed", Text = profile.InstallPath, PlaceholderText = "Folder that contains the game" };
        AutomationProperties.SetAutomationId(installBox, "Setup_install_path");
        var installNote = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Text = "Used to find out what kind of drive the game runs from (SSD or hard disk, NVMe or SATA) and how full it is." };
        var detect = new Button { Content = "Find it automatically (Steam)" };
        var browse = new Button { Content = "Browse..." };
        detect.Click += (_, _) =>
        {
            var found = GameLocator.FindSteamInstall(AppServices.Probe, capture.Result.Info.Game);
            installNote.Text = found is null ? "Not found automatically. Use Browse to pick the folder." : "Found it in your Steam library.";
            if (found is not null)
            {
                installBox.Text = found;
            }
        };
        browse.Click += async (_, _) =>
        {
            var folder = await Pickers.PickFolderAsync();
            if (folder is not null)
            {
                installBox.Text = folder;
            }
        };
        panel.Children.Add(installBox);
        panel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { detect, browse } });
        panel.Children.Add(installNote);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Your PC setup",
            Content = new ScrollViewer { Content = panel, MaxHeight = 560 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return false;
        }

        var values = new Dictionary<string, string>();
        foreach (var field in SetupFields.All)
        {
            var text = controls[field.Key] switch
            {
                ComboBox c => c.SelectedItem?.ToString() ?? "",
                TextBox t => t.Text.Trim(),
                _ => "",
            };
            if (text.Length > 0)
            {
                values[field.Key] = text;
            }
        }

        AppServices.Setup.Save(new UserSetupData { Values = values });
        profile.InstallPath = installBox.Text.Trim();
        AppServices.Profiles.Save(profile);
        return true;
    }
}
