using Daignostic.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Daignostic.Pages;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = new SettingsViewModel();
        InitializeComponent();
        ViewModel.PropertyChanged += (_, e) =>
        {
            // After a save the view model clears the pending key; mirror that in the box.
            if (e.PropertyName == nameof(SettingsViewModel.NewApiKey) && ViewModel.NewApiKey.Length == 0)
            {
                KeyBox.Password = "";
            }
        };
    }

    public SettingsViewModel ViewModel { get; }

    private void KeyBox_PasswordChanged(object sender, RoutedEventArgs e) => ViewModel.NewApiKey = KeyBox.Password;
}
