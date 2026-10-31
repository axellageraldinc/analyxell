using Analyxell.Core.Config;
using Analyxell.Dialogs;
using Analyxell.Services;
using Analyxell.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Analyxell.Pages;

public sealed partial class LibraryPage : Page
{
    public LibraryPage()
    {
        ViewModel = new LibraryViewModel();
        ViewModel.Refresh();
        InitializeComponent();
        PageLayout.Constrain(Scroller, PageContent, 1240);
        Loaded += async (_, _) => await ViewModel.ScanAsync();
    }

    public LibraryViewModel ViewModel { get; }

    private async void Rescan_Click(object sender, RoutedEventArgs e) => await ViewModel.ScanAsync();

    /// <summary>Only for a game no launcher knows about: it needs just a name.</summary>
    private async void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { Header = "Game name", PlaceholderText = "For example: Cyberpunk 2077" };
        AutomationProperties.SetAutomationId(nameBox, "AddGameName");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Add a game",
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 10,
                MinWidth = 420,
                Children =
                {
                    nameBox,
                    new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.75,
                        FontSize = 12,
                        MaxWidth = 420,
                        Text = "Use the name the game shows in CapFrameX captures, so captures are matched to it. Nothing else is needed.",
                    },
                },
            },
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary || nameBox.Text.Trim().Length == 0)
        {
            return;
        }

        AppServices.Profiles.EnsureSeen("", nameBox.Text.Trim());
        ViewModel.Refresh();
        await SetUpAsync(nameBox.Text.Trim());
    }

    private async void Setup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string game })
        {
            await SetUpAsync(game);
        }
    }

    private async Task SetUpAsync(string game)
    {
        var known = AppServices.Profiles.FindFor("", game);
        var hint = ViewModel.Games.FirstOrDefault(g => g.Game.Equals(game, StringComparison.OrdinalIgnoreCase))?.InstallFolder ?? "";
        await GameSetupDialog.RunAsync(
            XamlRoot,
            known?.Game.Length > 0 ? known.Game : game,
            known?.ProcessName ?? "",
            hint,
            profile => GameSettingsDialog.ShowForGameAsync(XamlRoot, profile));
        ViewModel.Refresh();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string game } && AppServices.Profiles.FindFor("", game) is { } profile)
        {
            AppServices.Profiles.Delete(profile.Key);
            ViewModel.Refresh();
        }
    }
}
