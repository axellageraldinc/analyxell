using Daignostic.Core.Config;
using Daignostic.Dialogs;
using Daignostic.Services;
using Daignostic.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Daignostic.Pages;

public sealed partial class LibraryPage : Page
{
    public LibraryPage()
    {
        ViewModel = new LibraryViewModel();
        ViewModel.Refresh();
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.ScanAsync();
    }

    public LibraryViewModel ViewModel { get; }

    private async void Rescan_Click(object sender, RoutedEventArgs e) => await ViewModel.ScanAsync();

    private async void AddDetected_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LibraryRow row })
        {
            await AddGameAsync(row.Game, row.InstallFolder);
        }
    }

    private async void AddGame_Click(object sender, RoutedEventArgs e) => await AddGameAsync("", "");

    private async Task AddGameAsync(string suggestedName, string suggestedFolder)
    {
        var nameBox = new TextBox { Header = "Game name", Text = suggestedName, PlaceholderText = "For example: Cyberpunk 2077" };
        AutomationProperties.SetAutomationId(nameBox, "AddGameName");
        var exeBox = new TextBox { Header = "Game file (.exe)", PlaceholderText = "The program that runs the game", IsReadOnly = true };
        AutomationProperties.SetAutomationId(exeBox, "AddGameExe");
        var browse = new Button { Content = "Choose the game's file..." };
        AutomationProperties.SetAutomationId(browse, "AddGameBrowse");
        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            FontSize = 12,
            Text = "daignostic only reads the file's name and folder: the name matches captures to this game, and the folder tells it what kind of drive the game is on. Nothing is run or changed.",
        };

        string process = "", folder = suggestedFolder;
        browse.Click += async (_, _) =>
        {
            if (await Pickers.PickExecutableAsync() is { } path)
            {
                process = Path.GetFileName(path);
                folder = suggestedFolder.Length > 0 ? suggestedFolder : Path.GetDirectoryName(path) ?? "";
                exeBox.Text = path;
                if (nameBox.Text.Trim().Length == 0)
                {
                    nameBox.Text = GuessName(path);
                }
            }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add a game",
            PrimaryButtonText = "Add game",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel { Spacing = 10, MinWidth = 440, Children = { nameBox, exeBox, browse, note } },
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || process.Length == 0)
        {
            return;
        }

        var profile = AppServices.Profiles.Load(process) ?? new GameProfile { ProcessName = process };
        profile.Game = nameBox.Text.Trim().Length > 0 ? nameBox.Text.Trim() : GuessName(process);
        profile.InstallPath = folder;
        AppServices.Profiles.Save(profile);
        ViewModel.Refresh();

        // Settings are optional; offer them straight away.
        if (await GameSettingsDialog.ShowForGameAsync(XamlRoot, profile))
        {
            ViewModel.Refresh();
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileFor(sender) is { } profile && await GameSettingsDialog.ShowForGameAsync(XamlRoot, profile))
        {
            ViewModel.Refresh();
        }
    }

    private async void InstallFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileFor(sender) is { } profile && await Pickers.PickFolderAsync() is { } folder)
        {
            profile.InstallPath = folder;
            AppServices.Profiles.Save(profile);
            ViewModel.Refresh();
        }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string process })
        {
            AppServices.Profiles.Delete(process);
            ViewModel.Refresh();
        }
    }

    private static GameProfile? ProfileFor(object sender) =>
        sender is FrameworkElement { Tag: string process } && process.Length > 0 ? AppServices.Profiles.Load(process) : null;

    private static string GuessName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        return name.Replace('_', ' ').Replace('-', ' ').Trim();
    }
}
