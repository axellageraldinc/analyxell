using Daignostic.Pages;
using Daignostic.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daignostic;

/// <summary>The application window: a navigation shell hosting the Analyze, Compare and Settings pages.</summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 900));

        Nav.SelectedItem = Nav.MenuItems[0];
        AppNavigation.Requested += page => DispatcherQueue.TryEnqueue(() => NavigateTo(page));
    }

    private void NavigateTo(string page)
    {
        if (page == "settings")
        {
            Nav.SelectedItem = Nav.SettingsItem;
        }
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var page = args.IsSettingsSelected
            ? typeof(SettingsPage)
            : (args.SelectedItemContainer?.Tag as string) switch
            {
                "compare" => typeof(ComparePage),
                "library" => typeof(LibraryPage),
                _ => typeof(AnalyzePage),
            };

        if (ContentFrame.CurrentSourcePageType != page)
        {
            ContentFrame.Navigate(page);
        }
    }
}
