using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daignostic.Core.Config;
using Daignostic.Services;

namespace Daignostic.ViewModels;

/// <summary>One game in the Library, ready to show.</summary>
public sealed record LibraryRow(
    string Game,
    string ProcessName,
    string StatusText,
    string Detail,
    string InstallText,
    IReadOnlyList<SettingRow> Settings,
    bool HasSettings,
    bool CanEdit,
    bool IsUserAdded);

/// <summary>
/// Lists every game daignostic knows, so a user can see that the game they want to diagnose is covered, what is known
/// about its settings, and add a game of their own.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    public ObservableCollection<LibraryRow> Games { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int Count { get; set; }

    public bool IsEmpty => Count == 0;

    public void Refresh()
    {
        var entries = GameLibrary.Build(AppServices.Profiles.List(), AppServices.ConfigReaders);
        Games.Clear();
        foreach (var e in entries)
        {
            Games.Add(new LibraryRow(
                e.Game,
                e.ProcessName,
                e.StatusText,
                e.Detail,
                e.InstallPath.Length > 0 ? "Installed in " + e.InstallPath : "Install folder not set",
                e.Settings.Select(s => new SettingRow(s.Label, s.Value, s.Source)).ToList(),
                e.Settings.Count > 0,
                CanEdit: e.ProcessName.Length > 0,
                e.IsUserAdded));
        }

        Count = Games.Count;
    }
}
