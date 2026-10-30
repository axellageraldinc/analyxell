using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Daignostic.Core.Config;
using Daignostic.Core.SystemInfo;
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
    bool IsUserAdded,
    bool IsDetectedOnly = false,
    string InstallFolder = "",
    string LookedUp = "",
    bool HasLookedUp = false);

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

    [ObservableProperty]
    public partial bool IsScanning { get; set; }

    [ObservableProperty]
    public partial string ScanStatus { get; set; } = "";

    private IReadOnlyList<InstalledGame> _detected = [];

    /// <summary>Asks Steam, Epic and GOG which games are installed, on any drive. Reads only the launchers' manifest files.</summary>
    public async Task ScanAsync()
    {
        if (IsScanning)
        {
            return;
        }

        IsScanning = true;
        ScanStatus = "Looking for installed games (Steam, Epic, GOG)...";
        try
        {
            _detected = await Task.Run(() => InstalledGameFinder.Find(AppServices.Probe));
            ScanStatus = _detected.Count == 0
                ? "No installed games were found through Steam, Epic or GOG. Add a game yourself if it comes from somewhere else."
                : $"Found {_detected.Count} installed game(s) through the launchers, on any drive.";
            Refresh();
        }
        finally
        {
            IsScanning = false;
        }
    }

    private static string LookedUpText(string processName)
    {
        if (processName.Length == 0 || AppServices.Profiles.Load(processName) is not { GameFacts.Count: > 0 } profile)
        {
            return "";
        }

        var offers = string.Join("; ", profile.GameFacts.Where(f => f.Key is "upscaling" or "frame_generation" or "vsync_modes" or "fps_cap").Select(f => $"{f.Key.Replace('_', ' ')}: {f.Value}"));
        return "Looked up on PCGamingWiki (" + profile.FactsSource + "). The game offers: " + offers + (profile.ConfigPath.Length > 0 ? ". Settings file found: " + profile.ConfigPath : ".");
    }

    public void Refresh()
    {
        var entries = GameLibrary.Build(AppServices.Profiles.List(), AppServices.ConfigReaders, _detected);
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
                e.IsUserAdded,
                IsDetectedOnly: e.Status == LibraryStatus.Detected,
                InstallFolder: e.InstallPath,
                LookedUp: LookedUpText(e.ProcessName),
                HasLookedUp: LookedUpText(e.ProcessName).Length > 0));
        }

        Count = Games.Count;
    }
}
