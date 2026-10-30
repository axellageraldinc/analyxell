using System.Text.Json;
using System.Text.RegularExpressions;

namespace Analyxell.Core.SystemInfo;

/// <summary>A game a launcher says is installed, on any drive.</summary>
public sealed record InstalledGame(string Name, string Folder, string Launcher)
{
    /// <summary>The drive the game is on, for example "D:".</summary>
    public string Drive => Folder.Length >= 2 && Folder[1] == ':' ? Folder[..2].ToUpperInvariant() : "";
}

/// <summary>
/// Lists installed games by asking the launchers that installed them (Steam, Epic, GOG). That reaches every drive the
/// launchers use without crawling the user's disks: it is fast, finds only real installs, and reads nothing but the
/// launchers' own small manifest files.
/// </summary>
public static partial class InstalledGameFinder
{
    private static readonly string[] NotGames =
    [
        "steamworks common redistributables", "steam linux runtime", "proton", "steamvr", "wallpaper engine", "source sdk",
        "dedicated server", "directx", "redistributable", "dsx", "game compressor", "soundpad",
    ];

    public static IReadOnlyList<InstalledGame> Find(ISystemProbe probe)
    {
        var games = new List<InstalledGame>();
        games.AddRange(Steam(probe));
        games.AddRange(Epic(probe));
        games.AddRange(Gog(probe));

        return games
            .Where(g => g.Name.Length > 0 && !NotGames.Any(n => g.Name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(g => Normalize(g.Name))
            .Select(g => g.First())
            .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Same normalisation the rest of the app uses to match a game by name.</summary>
    public static string Normalize(string s) => new(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static IEnumerable<InstalledGame> Steam(ISystemProbe probe)
    {
        foreach (var library in GameLocator.SteamLibraries(probe))
        {
            var apps = Path.Combine(library, "steamapps");
            foreach (var manifest in probe.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                var text = probe.ReadText(manifest);
                if (text is null)
                {
                    continue;
                }

                var name = AcfName().Match(text);
                var dir = AcfInstallDir().Match(text);
                if (!name.Success || !dir.Success)
                {
                    continue;
                }

                var folder = Path.Combine(apps, "common", dir.Groups[1].Value);
                if (probe.DirectoryExists(folder))
                {
                    yield return new InstalledGame(name.Groups[1].Value, folder, "Steam");
                }
            }
        }
    }

    private static IEnumerable<InstalledGame> Epic(ISystemProbe probe)
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        foreach (var file in probe.EnumerateFiles(manifests, "*.item"))
        {
            var found = ParseEpic(probe.ReadText(file));
            if (found is not null && probe.DirectoryExists(found.Folder))
            {
                yield return found;
            }
        }
    }

    internal static InstalledGame? ParseEpic(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
            var folder = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
            return string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(folder) ? null : new InstalledGame(name, folder, "Epic Games");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<InstalledGame> Gog(ISystemProbe probe)
    {
        const string key = @"SOFTWARE\WOW6432Node\GOG.com\Games";
        foreach (var id in probe.RegistrySubKeyNames("HKLM", key))
        {
            var name = probe.RegistryString("HKLM", key + "\\" + id, "gameName");
            var path = probe.RegistryString("HKLM", key + "\\" + id, "path");
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path) && probe.DirectoryExists(path))
            {
                yield return new InstalledGame(name, path, "GOG");
            }
        }
    }

    [GeneratedRegex("\"name\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AcfName();

    [GeneratedRegex("\"installdir\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AcfInstallDir();
}
